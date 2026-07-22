using System;

namespace CodexQuotaPet
{
    internal sealed class DashboardService
    {
        private readonly AppSettings _settings;
        private DashboardSnapshot _lastGood;

        public DashboardService(AppSettings settings)
        {
            _settings = settings;
        }

        public DashboardSnapshot Refresh()
        {
            DateTime attempted = DateTime.Now;
            string codexHome = CodexLocator.FindCodexHome(_settings.CodexHome);
            // A seven-day quota window can touch eight calendar dates when the reset
            // occurs mid-day. Scan eight so the current-cycle estimate does not drop
            // the first partial day; the chart independently displays only the last 7.
            LocalUsageSnapshot local = new LocalUsageScanner().Scan(codexHome, 8);
            try
            {
                string executable = CodexLocator.FindExecutable(_settings.CodexExecutable);
                QuotaSnapshot quota = new CodexAppServerClient(executable, 15000).ReadSnapshot();
                DashboardSnapshot fresh = new DashboardSnapshot
                {
                    Quota = quota,
                    LocalUsage = local,
                    AttemptedAt = attempted,
                    IsStale = false
                };
                _lastGood = fresh;
                return fresh;
            }
            catch (Exception ex)
            {
                QuotaSnapshot localQuota = BuildLocalQuotaFallback(local);
                QuotaSnapshot fallbackQuota = MergeFallbackQuota(_lastGood == null ? null : _lastGood.Quota, localQuota);
                return new DashboardSnapshot
                {
                    Quota = fallbackQuota,
                    LocalUsage = local,
                    AttemptedAt = attempted,
                    IsStale = fallbackQuota != null,
                    ErrorMessage = fallbackQuota == null
                        ? CodexAppServerClient.Sanitize(ex.Message)
                        : "实时额度暂不可用，显示最近本地快照"
                };
            }
        }

        private static QuotaSnapshot BuildLocalQuotaFallback(LocalUsageSnapshot local)
        {
            if (local == null || (local.LatestPrimary == null && local.LatestSecondary == null)) return null;
            return new QuotaSnapshot
            {
                Primary = local.LatestPrimary,
                Secondary = local.LatestSecondary,
                PlanType = local.LatestPlanType,
                RefreshedAt = local.LatestRateLimitCapturedAt ?? DateTime.Now
            };
        }

        internal static QuotaSnapshot MergeFallbackQuota(QuotaSnapshot lastGood, QuotaSnapshot local)
        {
            if (lastGood == null) return local;
            if (local == null || local.RefreshedAt <= lastGood.RefreshedAt) return lastGood;

            // Session token_count events often carry a newer rate_limits snapshot than
            // the most recent successful app-server call. Refresh the windows from it,
            // while retaining fields that session logs do not contain.
            return new QuotaSnapshot
            {
                Primary = local.Primary ?? lastGood.Primary,
                Secondary = local.Secondary ?? lastGood.Secondary,
                ResetCreditsAvailable = lastGood.ResetCreditsAvailable,
                PlanType = local.PlanType ?? lastGood.PlanType,
                CreditsBalance = lastGood.CreditsBalance,
                CreditsUnlimited = lastGood.CreditsUnlimited,
                AccountTodayTokens = lastGood.AccountTodayTokens,
                LifetimeTokens = lastGood.LifetimeTokens,
                PeakDailyTokens = lastGood.PeakDailyTokens,
                CurrentStreakDays = lastGood.CurrentStreakDays,
                AccountDailyUsage = lastGood.AccountDailyUsage,
                RefreshedAt = local.RefreshedAt,
                CodexVersion = lastGood.CodexVersion
            };
        }

    }
}
