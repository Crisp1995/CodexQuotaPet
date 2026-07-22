using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodexQuotaPet
{
    internal sealed class QuotaWindowData
    {
        public int UsedPercent { get; set; }
        public long? WindowDurationMinutes { get; set; }
        public DateTime? ResetsAtLocal { get; set; }

        public int RemainingPercent
        {
            get { return Math.Max(0, Math.Min(100, 100 - UsedPercent)); }
        }

        public string Label
        {
            get
            {
                if (!WindowDurationMinutes.HasValue) return "额度窗口";
                long minutes = WindowDurationMinutes.Value;
                if (minutes == 300) return "5 小时";
                if (minutes % 10080 == 0) return (minutes / 10080).ToString(CultureInfo.InvariantCulture) + " 周";
                if (minutes % 1440 == 0) return (minutes / 1440).ToString(CultureInfo.InvariantCulture) + " 天";
                if (minutes % 60 == 0) return (minutes / 60).ToString(CultureInfo.InvariantCulture) + " 小时";
                return minutes.ToString(CultureInfo.InvariantCulture) + " 分钟";
            }
        }
    }

    internal sealed class DailyUsage
    {
        public DateTime Date { get; set; }
        public long Tokens { get; set; }
    }

    internal sealed class LocalUsageSnapshot
    {
        public long TodayTokens { get; set; }
        public int TodayRequests { get; set; }
        public DateTime? LastRequestAt { get; set; }
        public QuotaWindowData LatestPrimary { get; set; }
        public QuotaWindowData LatestSecondary { get; set; }
        public string LatestPlanType { get; set; }
        public DateTime? LatestRateLimitCapturedAt { get; set; }
        public List<DailyUsage> Daily { get; set; }

        public LocalUsageSnapshot()
        {
            Daily = new List<DailyUsage>();
        }
    }

    internal sealed class QuotaSnapshot
    {
        public QuotaWindowData Primary { get; set; }
        public QuotaWindowData Secondary { get; set; }
        public int? ResetCreditsAvailable { get; set; }
        public string PlanType { get; set; }
        public string CreditsBalance { get; set; }
        public bool CreditsUnlimited { get; set; }
        public long? AccountTodayTokens { get; set; }
        public long? LifetimeTokens { get; set; }
        public long? PeakDailyTokens { get; set; }
        public long? CurrentStreakDays { get; set; }
        public List<DailyUsage> AccountDailyUsage { get; set; }
        public DateTime RefreshedAt { get; set; }
        public string CodexVersion { get; set; }

        public QuotaSnapshot()
        {
            AccountDailyUsage = new List<DailyUsage>();
        }

        public QuotaWindowData PreferredWindow
        {
            get { return Primary ?? Secondary; }
        }
    }

    internal sealed class DashboardSnapshot
    {
        public QuotaSnapshot Quota { get; set; }
        public LocalUsageSnapshot LocalUsage { get; set; }
        public DateTime AttemptedAt { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsStale { get; set; }

        public bool IsSuccess
        {
            get { return Quota != null; }
        }

        public long TodayTokens
        {
            get
            {
                if (HasFreshLocalUsage) return LocalUsage.TodayTokens;
                if (Quota != null && Quota.AccountTodayTokens.HasValue) return Quota.AccountTodayTokens.Value;
                return LocalUsage == null ? 0 : LocalUsage.TodayTokens;
            }
        }

        public string UsageSource
        {
            get
            {
                if (HasFreshLocalUsage) return "本机会话实时";
                if (Quota != null && Quota.AccountTodayTokens.HasValue) return "账户用量";
                return "本机会话";
            }
        }

        public int TodayRequests
        {
            get { return LocalUsage == null ? 0 : LocalUsage.TodayRequests; }
        }

        public long AverageTokensPerRequest
        {
            get { return TodayRequests <= 0 ? 0 : TodayTokens / TodayRequests; }
        }

        public DateTime? CurrentCycleStartedAt
        {
            get
            {
                QuotaWindowData window = Quota == null ? null : Quota.PreferredWindow;
                if (window == null || !window.ResetsAtLocal.HasValue || !window.WindowDurationMinutes.HasValue) return null;
                return window.ResetsAtLocal.Value.AddMinutes(-window.WindowDurationMinutes.Value);
            }
        }

        public long CurrentCycleTokens
        {
            get
            {
                DateTime start = CurrentCycleStartedAt ?? DateTime.Today.AddDays(-6);
                DateTime today = DateTime.Today;
                long total = 0;
                foreach (DailyUsage item in PreferredDailyUsage)
                    if (item.Date.Date >= start.Date && item.Date.Date <= today) total += Math.Max(0, item.Tokens);
                return total;
            }
        }

        public IList<DailyUsage> PreferredDailyUsage
        {
            get
            {
                if (HasFreshLocalUsage) return LocalUsage.Daily;
                if (Quota != null && Quota.AccountDailyUsage != null && Quota.AccountDailyUsage.Count > 0)
                    return Quota.AccountDailyUsage;
                if (LocalUsage != null) return LocalUsage.Daily;
                return new List<DailyUsage>();
            }
        }

        private bool HasFreshLocalUsage
        {
            get
            {
                if (LocalUsage == null) return false;
                if (LocalUsage.TodayRequests > 0 || LocalUsage.TodayTokens > 0) return true;
                if (LocalUsage.Daily == null) return false;
                foreach (DailyUsage item in LocalUsage.Daily)
                    if (item != null && item.Tokens > 0) return true;
                return false;
            }
        }
    }

    internal static class UsageSeries
    {
        internal static IList<DailyUsage> LastCalendarDays(IEnumerable<DailyUsage> source, DateTime today, int days)
        {
            int count = Math.Max(1, days);
            DateTime end = today.Date;
            DateTime start = end.AddDays(-(count - 1));
            Dictionary<DateTime, long> totals = new Dictionary<DateTime, long>();
            if (source != null)
            {
                foreach (DailyUsage item in source)
                {
                    if (item == null) continue;
                    DateTime date = item.Date.Date;
                    if (date < start || date > end) continue;
                    long existing;
                    totals.TryGetValue(date, out existing);
                    totals[date] = existing + Math.Max(0, item.Tokens);
                }
            }

            List<DailyUsage> result = new List<DailyUsage>();
            for (int i = 0; i < count; i++)
            {
                DateTime date = start.AddDays(i);
                long tokens;
                totals.TryGetValue(date, out tokens);
                result.Add(new DailyUsage { Date = date, Tokens = tokens });
            }
            return result;
        }
    }
}
