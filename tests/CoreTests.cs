using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace CodexQuotaPet.Tests
{
    internal static class TestEntry
    {
        private static int _passed;

        public static int Main()
        {
            try
            {
                Run("额度响应解析", TestRateLimitParsing);
                Run("账户 Token 桶解析", TestUsageParsing);
                Run("本机会话增量统计", TestLocalUsageScanner);
                Run("实时本地用量优先", TestFreshLocalUsagePriority);
                Run("额度失败后采用更新快照", TestNewerLocalQuotaFallback);
                Run("趋势固定最近七日", TestRecentSevenDays);
                Run("设置保存与归一化", TestSettingsRoundTrip);
                Run("低额度颜色阈值", TestQuotaColors);
                Console.WriteLine("PASS: " + _passed + " tests");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex.Message);
                return 1;
            }
        }

        private static void TestRateLimitParsing()
        {
            Dictionary<string, object> account = Map("{\"account\":{\"planType\":\"plus\"}}");
            Dictionary<string, object> limits = Map("{\"rateLimits\":{\"primary\":{\"usedPercent\":32,\"windowDurationMins\":10080,\"resetsAt\":1780000000},\"secondary\":null},\"rateLimitResetCredits\":{\"availableCount\":2}}");
            QuotaSnapshot result = CodexAppServerClient.Parse(account, limits, null);
            Equal(68, result.Primary.RemainingPercent, "remaining percent");
            Equal("1 周", result.Primary.Label, "window label");
            Equal(2, result.ResetCreditsAvailable.Value, "reset credits");
            Equal("plus", result.PlanType, "plan type");
        }

        private static void TestUsageParsing()
        {
            string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Dictionary<string, object> account = Map("{\"account\":{\"planType\":\"pro\"}}");
            Dictionary<string, object> limits = Map("{\"rateLimits\":{\"primary\":{\"usedPercent\":1}}}");
            Dictionary<string, object> usage = Map("{\"summary\":{\"lifetimeTokens\":123456,\"peakDailyTokens\":9000},\"dailyUsageBuckets\":[{\"startDate\":\"" + today + "\",\"tokens\":4321}]}");
            QuotaSnapshot result = CodexAppServerClient.Parse(account, limits, usage);
            Equal(4321L, result.AccountTodayTokens.Value, "today tokens");
            Equal(123456L, result.LifetimeTokens.Value, "lifetime tokens");
        }

        private static void TestLocalUsageScanner()
        {
            string root = Path.Combine(Path.GetTempPath(), "CodexQuotaPetTests-" + Guid.NewGuid().ToString("N"));
            string dateFolder = Path.Combine(root, "sessions", DateTime.Today.ToString("yyyy"), DateTime.Today.ToString("MM"), DateTime.Today.ToString("dd"));
            Directory.CreateDirectory(dateFolder);
            string timestamp = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
            string[] lines =
            {
                "{\"timestamp\":\"" + timestamp + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"user_message\"}}",
                "{\"timestamp\":\"" + timestamp + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"total_tokens\":100}}}}",
                "{\"timestamp\":\"" + timestamp + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"total_tokens\":250}},\"rate_limits\":{\"plan_type\":\"plus\",\"primary\":{\"used_percent\":4.0,\"window_minutes\":10080,\"resets_at\":1780000000}}}}"
            };
            File.WriteAllLines(Path.Combine(dateFolder, "sample.jsonl"), lines);
            try
            {
                LocalUsageSnapshot result = new LocalUsageScanner().Scan(root, 7);
                Equal(250L, result.TodayTokens, "incremental token total");
                Equal(1, result.TodayRequests, "request count");
                Equal(96, result.LatestPrimary.RemainingPercent, "local quota fallback");
                Equal("plus", result.LatestPlanType, "local plan fallback");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestSettingsRoundTrip()
        {
            string root = Path.Combine(Path.GetTempPath(), "CodexQuotaPetSettings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                SettingsStore store = new SettingsStore(root);
                AppSettings settings = new AppSettings { RefreshIntervalSeconds = 1, WarningThreshold = 10, CriticalThreshold = 20, DetailedMode = true };
                store.Save(settings);
                AppSettings loaded = store.Load();
                Equal(30, loaded.RefreshIntervalSeconds, "minimum refresh");
                Equal(20, loaded.CriticalThreshold, "critical threshold");
                Equal(21, loaded.WarningThreshold, "warning above critical");
                Equal(true, loaded.DetailedMode, "mode persisted");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestFreshLocalUsagePriority()
        {
            DashboardSnapshot snapshot = new DashboardSnapshot
            {
                Quota = new QuotaSnapshot
                {
                    AccountTodayTokens = 100,
                    AccountDailyUsage = new List<DailyUsage>
                    {
                        new DailyUsage { Date = DateTime.Today, Tokens = 100 }
                    },
                    Primary = new QuotaWindowData
                    {
                        UsedPercent = 10,
                        WindowDurationMinutes = 10080,
                        ResetsAtLocal = DateTime.Now.AddDays(1)
                    }
                },
                LocalUsage = new LocalUsageSnapshot
                {
                    TodayTokens = 250,
                    TodayRequests = 1,
                    Daily = new List<DailyUsage>
                    {
                        new DailyUsage { Date = DateTime.Today, Tokens = 250 }
                    }
                }
            };
            Equal(250L, snapshot.TodayTokens, "live today tokens");
            Equal(250L, snapshot.CurrentCycleTokens, "live cycle tokens");
            Equal("本机会话实时", snapshot.UsageSource, "live usage source");
        }

        private static void TestRecentSevenDays()
        {
            DateTime today = new DateTime(2026, 7, 22);
            IList<DailyUsage> result = UsageSeries.LastCalendarDays(new List<DailyUsage>
            {
                new DailyUsage { Date = new DateTime(2026, 3, 16), Tokens = 999 },
                new DailyUsage { Date = today.AddDays(-6), Tokens = 10 },
                new DailyUsage { Date = today.AddDays(-2), Tokens = 20 },
                new DailyUsage { Date = today.AddDays(-2), Tokens = 5 },
                new DailyUsage { Date = today.AddDays(1), Tokens = 888 }
            }, today, 7);
            Equal(7, result.Count, "seven calendar buckets");
            Equal(today.AddDays(-6), result[0].Date, "first date");
            Equal(today, result[6].Date, "last date");
            Equal(25L, result[4].Tokens, "duplicate day total");
        }

        private static void TestNewerLocalQuotaFallback()
        {
            DateTime oldTime = new DateTime(2026, 7, 22, 10, 0, 0);
            QuotaSnapshot lastGood = new QuotaSnapshot
            {
                Primary = new QuotaWindowData { UsedPercent = 20 },
                ResetCreditsAvailable = 2,
                PlanType = "pro",
                RefreshedAt = oldTime
            };
            QuotaSnapshot local = new QuotaSnapshot
            {
                Primary = new QuotaWindowData { UsedPercent = 31 },
                PlanType = "prolite",
                RefreshedAt = oldTime.AddMinutes(1)
            };
            QuotaSnapshot merged = DashboardService.MergeFallbackQuota(lastGood, local);
            Equal(69, merged.Primary.RemainingPercent, "newer local quota");
            Equal(2, merged.ResetCreditsAvailable.Value, "preserve reset credits");
            Equal("prolite", merged.PlanType, "newer plan type");
        }


        private static void TestQuotaColors()
        {
            System.Windows.Media.SolidColorBrush critical = QuotaRing.GetAccentBrush(10, 30, 15) as System.Windows.Media.SolidColorBrush;
            System.Windows.Media.SolidColorBrush warning = QuotaRing.GetAccentBrush(20, 30, 15) as System.Windows.Media.SolidColorBrush;
            Equal("#FFFF5270", critical.Color.ToString(), "critical red");
            Equal("#FFFFB149", warning.Color.ToString(), "warning amber");
        }

        private static Dictionary<string, object> Map(string json)
        {
            return new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
        }

        private static void Run(string name, Action test)
        {
            test();
            _passed++;
            Console.WriteLine("  OK " + name);
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(label + ": expected " + expected + ", got " + actual);
        }
    }
}
