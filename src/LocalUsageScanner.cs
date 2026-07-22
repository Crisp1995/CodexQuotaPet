using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace CodexQuotaPet
{
    internal sealed class LocalUsageScanner
    {
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };

        public LocalUsageSnapshot Scan(string codexHome, int days)
        {
            LocalUsageSnapshot result = new LocalUsageSnapshot();
            DateTime firstDay = DateTime.Today.AddDays(-(Math.Max(1, days) - 1));
            Dictionary<DateTime, long> totals = new Dictionary<DateTime, long>();
            for (int i = 0; i < days; i++) totals[firstDay.AddDays(i)] = 0;

            string sessions = Path.Combine(codexHome ?? string.Empty, "sessions");
            if (!Directory.Exists(sessions))
            {
                FillDaily(result, totals);
                return result;
            }

            HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < days; i++)
            {
                DateTime date = firstDay.AddDays(i);
                string folder = Path.Combine(sessions, date.ToString("yyyy", CultureInfo.InvariantCulture),
                    date.ToString("MM", CultureInfo.InvariantCulture), date.ToString("dd", CultureInfo.InvariantCulture));
                if (!Directory.Exists(folder)) continue;
                try
                {
                    foreach (string file in Directory.GetFiles(folder, "*.jsonl", SearchOption.AllDirectories)) files.Add(file);
                }
                catch { }
            }

            foreach (string file in files) ScanFile(file, firstDay, totals, result);
            FillDaily(result, totals);
            result.TodayTokens = totals.ContainsKey(DateTime.Today) ? totals[DateTime.Today] : 0;
            return result;
        }

        private void ScanFile(string path, DateTime firstDay, Dictionary<DateTime, long> totals, LocalUsageSnapshot result)
        {
            long previousTotal = 0;
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length < 20 || line.Length > 8 * 1024 * 1024) continue;
                        Dictionary<string, object> root;
                        try { root = _json.DeserializeObject(line) as Dictionary<string, object>; }
                        catch { continue; }
                        if (root == null || !string.Equals(JsonValue.String(root, "type"), "event_msg", StringComparison.Ordinal)) continue;
                        Dictionary<string, object> payload = JsonValue.Child(root, "payload");
                        string type = JsonValue.String(payload, "type");
                        DateTime timestamp = ParseTimestamp(JsonValue.String(root, "timestamp"));

                        if (string.Equals(type, "user_message", StringComparison.Ordinal))
                        {
                            if (timestamp.Date == DateTime.Today) result.TodayRequests++;
                            if (!result.LastRequestAt.HasValue || timestamp > result.LastRequestAt.Value) result.LastRequestAt = timestamp;
                            continue;
                        }

                        if (!string.Equals(type, "token_count", StringComparison.Ordinal)) continue;
                        CaptureLatestRateLimits(JsonValue.Child(payload, "rate_limits"), timestamp, result);
                        Dictionary<string, object> info = JsonValue.Child(payload, "info");
                        Dictionary<string, object> totalUsage = JsonValue.Child(info, "total_token_usage");
                        long? current = JsonValue.Long(totalUsage, "total_tokens");
                        if (!current.HasValue) continue;
                        long delta = current.Value >= previousTotal ? current.Value - previousTotal : current.Value;
                        previousTotal = current.Value;
                        DateTime day = timestamp.Date;
                        if (day >= firstDay && totals.ContainsKey(day)) totals[day] += Math.Max(0, delta);
                    }
                }
            }
            catch
            {
                // A concurrently rotated or malformed session is ignored; other sessions still count.
            }
        }

        private static void CaptureLatestRateLimits(Dictionary<string, object> rateLimits, DateTime timestamp, LocalUsageSnapshot result)
        {
            if (rateLimits == null) return;
            if (result.LatestRateLimitCapturedAt.HasValue && timestamp <= result.LatestRateLimitCapturedAt.Value) return;
            QuotaWindowData primary = WindowFromLog(JsonValue.Child(rateLimits, "primary"));
            QuotaWindowData secondary = WindowFromLog(JsonValue.Child(rateLimits, "secondary"));
            if (primary == null && secondary == null) return;
            result.LatestPrimary = primary;
            result.LatestSecondary = secondary;
            result.LatestPlanType = JsonValue.String(rateLimits, "plan_type") ?? JsonValue.String(rateLimits, "planType");
            result.LatestRateLimitCapturedAt = timestamp;
        }

        private static QuotaWindowData WindowFromLog(Dictionary<string, object> map)
        {
            if (map == null) return null;
            double? used = JsonValue.Double(map, "used_percent") ?? JsonValue.Double(map, "usedPercent");
            if (!used.HasValue) return null;
            QuotaWindowData window = new QuotaWindowData();
            window.UsedPercent = Math.Max(0, Math.Min(100, (int)Math.Round(used.Value)));
            window.WindowDurationMinutes = JsonValue.Long(map, "window_minutes") ?? JsonValue.Long(map, "windowDurationMins");
            long? unix = JsonValue.Long(map, "resets_at") ?? JsonValue.Long(map, "resetsAt");
            if (unix.HasValue)
            {
                try { window.ResetsAtLocal = DateTimeOffset.FromUnixTimeSeconds(unix.Value).LocalDateTime; } catch { }
            }
            return window;
        }

        private static DateTime ParseTimestamp(string text)
        {
            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed)
                ? parsed.LocalDateTime
                : DateTime.Now;
        }

        private static void FillDaily(LocalUsageSnapshot result, Dictionary<DateTime, long> totals)
        {
            result.Daily = totals.OrderBy(pair => pair.Key)
                .Select(pair => new DailyUsage { Date = pair.Key, Tokens = pair.Value }).ToList();
        }
    }
}
