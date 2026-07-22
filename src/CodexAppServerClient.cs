using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexQuotaPet
{
    internal sealed class CodexAppServerClient
    {
        private readonly string _executable;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        private readonly int _timeoutMilliseconds;

        public CodexAppServerClient(string executable, int timeoutMilliseconds)
        {
            _executable = executable;
            _timeoutMilliseconds = timeoutMilliseconds;
        }

        public QuotaSnapshot ReadSnapshot()
        {
            if (string.IsNullOrWhiteSpace(_executable) || !File.Exists(_executable))
                throw new InvalidOperationException("未找到可执行的 Codex CLI。请安装独立 CLI 或在设置中指定 codex.exe。 ");

            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = _executable,
                Arguments = "-s read-only -a never app-server --listen stdio://",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };

            using (Process process = new Process { StartInfo = start })
            {
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(_timeoutMilliseconds);
                if (!process.Start()) throw new InvalidOperationException("Codex app-server 启动失败。");
                Task<string> stderr = process.StandardError.ReadToEndAsync();

                try
                {
                    Dictionary<string, object> initializeParams = new Dictionary<string, object>();
                    initializeParams["clientInfo"] = new Dictionary<string, object>
                    {
                        { "name", "codex-quota-pet" },
                        { "title", "Codex Quota Pet" },
                        { "version", "0.1.0" }
                    };
                    initializeParams["capabilities"] = new Dictionary<string, object>
                    {
                        { "experimentalApi", true }
                    };
                    Call(process, 1, "initialize", initializeParams, deadline);
                    Send(process, new Dictionary<string, object> { { "method", "initialized" } });

                    // Quota is the critical path. Do not let optional account metadata delay it.
                    Dictionary<string, object> limits = Call(process, 2, "account/rateLimits/read", null, deadline);

                    Dictionary<string, object> usage = null;
                    DateTime usageDeadline = DateTime.UtcNow.AddSeconds(8);
                    if (usageDeadline > deadline) usageDeadline = deadline;
                    try { usage = Call(process, 3, "account/usage/read", null, usageDeadline); }
                    catch { /* Token activity is optional; quota remains usable. */ }

                    QuotaSnapshot snapshot = Parse(new Dictionary<string, object>(), limits, usage);
                    snapshot.RefreshedAt = DateTime.Now;
                    return snapshot;
                }
                finally
                {
                    try { process.StandardInput.Close(); } catch { }
                    if (!process.WaitForExit(750))
                    {
                        try { process.Kill(); } catch { }
                    }
                }
            }
        }

        private Dictionary<string, object> Call(Process process, int id, string method, object parameters, DateTime deadline)
        {
            Dictionary<string, object> request = new Dictionary<string, object>();
            request["id"] = id;
            request["method"] = method;
            if (parameters != null) request["params"] = parameters;
            Send(process, request);

            while (DateTime.UtcNow < deadline)
            {
                int remaining = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                Task<string> read = process.StandardOutput.ReadLineAsync();
                if (!read.Wait(remaining)) throw new TimeoutException("Codex app-server 响应超时。");
                string line = read.Result;
                if (line == null) throw new IOException("Codex app-server 已提前退出。");
                if (line.Length == 0) continue;

                Dictionary<string, object> message;
                try { message = _json.DeserializeObject(line) as Dictionary<string, object>; }
                catch { continue; }
                if (message == null) continue;
                int? responseId = JsonValue.Int(message, "id");
                if (!responseId.HasValue || responseId.Value != id) continue;

                Dictionary<string, object> error = JsonValue.Child(message, "error");
                if (error != null)
                    throw new InvalidOperationException("Codex 返回错误：" + Sanitize(JsonValue.String(error, "message")));

                return JsonValue.Child(message, "result") ?? new Dictionary<string, object>();
            }
            throw new TimeoutException("Codex app-server 响应超时。");
        }

        private void Send(Process process, Dictionary<string, object> message)
        {
            process.StandardInput.WriteLine(_json.Serialize(message));
            process.StandardInput.Flush();
        }

        internal static QuotaSnapshot Parse(Dictionary<string, object> accountResult,
            Dictionary<string, object> limitsResult, Dictionary<string, object> usageResult)
        {
            QuotaSnapshot snapshot = new QuotaSnapshot();

            Dictionary<string, object> account = JsonValue.Child(accountResult, "account");
            snapshot.PlanType = JsonValue.String(account, "planType");

            Dictionary<string, object> rateLimits = JsonValue.Child(limitsResult, "rateLimits");
            if (rateLimits == null)
            {
                Dictionary<string, object> byId = JsonValue.Child(limitsResult, "rateLimitsByLimitId");
                if (byId != null)
                {
                    object codex;
                    if (byId.TryGetValue("codex", out codex)) rateLimits = JsonValue.Object(codex);
                    if (rateLimits == null && byId.Count > 0) rateLimits = JsonValue.Object(byId.Values.First());
                }
            }

            if (rateLimits == null) throw new InvalidOperationException("Codex 未返回可识别的额度数据。");
            snapshot.Primary = ParseWindow(JsonValue.Child(rateLimits, "primary"));
            snapshot.Secondary = ParseWindow(JsonValue.Child(rateLimits, "secondary"));
            if (snapshot.Primary == null && snapshot.Secondary == null)
                throw new InvalidOperationException("当前账户没有可显示的额度窗口。");

            if (string.IsNullOrWhiteSpace(snapshot.PlanType)) snapshot.PlanType = JsonValue.String(rateLimits, "planType");
            Dictionary<string, object> credits = JsonValue.Child(rateLimits, "credits");
            if (credits != null)
            {
                snapshot.CreditsBalance = JsonValue.String(credits, "balance");
                snapshot.CreditsUnlimited = JsonValue.Bool(credits, "unlimited") ?? false;
            }

            Dictionary<string, object> resetCredits = JsonValue.Child(limitsResult, "rateLimitResetCredits");
            snapshot.ResetCreditsAvailable = JsonValue.Int(resetCredits, "availableCount");
            ParseUsage(usageResult, snapshot);
            return snapshot;
        }

        private static QuotaWindowData ParseWindow(Dictionary<string, object> map)
        {
            if (map == null) return null;
            int? used = JsonValue.Int(map, "usedPercent");
            if (!used.HasValue) return null;
            QuotaWindowData window = new QuotaWindowData();
            window.UsedPercent = Math.Max(0, Math.Min(100, used.Value));
            window.WindowDurationMinutes = JsonValue.Long(map, "windowDurationMins");
            long? unix = JsonValue.Long(map, "resetsAt");
            if (unix.HasValue)
            {
                try { window.ResetsAtLocal = DateTimeOffset.FromUnixTimeSeconds(unix.Value).LocalDateTime; }
                catch { }
            }
            return window;
        }

        private static void ParseUsage(Dictionary<string, object> usage, QuotaSnapshot snapshot)
        {
            if (usage == null) return;
            Dictionary<string, object> summary = JsonValue.Child(usage, "summary");
            snapshot.LifetimeTokens = JsonValue.Long(summary, "lifetimeTokens");
            snapshot.PeakDailyTokens = JsonValue.Long(summary, "peakDailyTokens");
            snapshot.CurrentStreakDays = JsonValue.Long(summary, "currentStreakDays");

            object[] buckets = JsonValue.Array(usage, "dailyUsageBuckets");
            if (buckets == null) return;
            DateTime today = DateTime.Today;
            foreach (object raw in buckets)
            {
                Dictionary<string, object> bucket = JsonValue.Object(raw);
                DateTime date;
                if (bucket == null || !DateTime.TryParseExact(JsonValue.String(bucket, "startDate"), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) continue;
                long tokens = JsonValue.Long(bucket, "tokens") ?? 0;
                snapshot.AccountDailyUsage.Add(new DailyUsage { Date = date, Tokens = Math.Max(0, tokens) });
                if (date.Date == today) snapshot.AccountTodayTokens = Math.Max(0, tokens);
            }
            snapshot.AccountDailyUsage = snapshot.AccountDailyUsage.OrderBy(item => item.Date).ToList();
        }

        internal static string Sanitize(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "未知错误";
            string cleaned = message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%");
            int bearer = cleaned.IndexOf("Bearer ", StringComparison.OrdinalIgnoreCase);
            if (bearer >= 0) cleaned = cleaned.Substring(0, bearer) + "Bearer [已隐藏]";
            return cleaned.Length <= 240 ? cleaned : cleaned.Substring(0, 240) + "…";
        }
    }
}
