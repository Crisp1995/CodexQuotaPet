using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Threading;

namespace CodexQuotaPet
{
    internal static class AppEntry
    {
        private const string MutexName = @"Local\CodexQuotaPet.SingleInstance.v1";
        private const string ShowEventName = @"Local\CodexQuotaPet.Show.v1";
        private const string StopEventName = @"Local\CodexQuotaPet.Stop.v1";

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                DiagnosticLog.Write("START application entry");
                return Run(args);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write("FATAL " + ex.GetType().Name + ": " + ex.Message);
                return 10;
            }
        }

        private static int Run(string[] args)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            SettingsStore store = new SettingsStore(baseDirectory);
            AppSettings settings = store.Load();

            if (HasArgument(args, "--check")) return RunCheck(settings);
            if (HasArgument(args, "--once")) return RunOnce(settings);
            if (HasArgument(args, "--stop")) return SignalEvent(StopEventName) ? 0 : 1;

            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    SignalEvent(ShowEventName);
                    return 0;
                }

                using (EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                using (EventWaitHandle stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, StopEventName))
                {
                    DiagnosticLog.Write("STAGE creating WPF application");
                    Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                    {
                        DiagnosticLog.Write("DISPATCHER " + e.Exception.GetType().Name + ": " + e.Exception.Message);
                    };
                    DiagnosticLog.Write("STAGE creating main window");
                    MainWindow window = new MainWindow(settings, store);
                    DiagnosticLog.Write("STAGE main window created");
                    RegisteredWaitHandle showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, delegate
                    {
                        app.Dispatcher.BeginInvoke(new Action(window.ShowAndActivate));
                    }, null, Timeout.Infinite, false);
                    RegisteredWaitHandle stopWait = ThreadPool.RegisterWaitForSingleObject(stopEvent, delegate
                    {
                        app.Dispatcher.BeginInvoke(new Action(window.ExitApplication));
                    }, null, Timeout.Infinite, false);

                    app.Exit += delegate
                    {
                        showWait.Unregister(null);
                        stopWait.Unregister(null);
                    };
                    DiagnosticLog.Write("STAGE showing main window");
                    window.Show();
                    DiagnosticLog.Write("STAGE entering WPF message loop");
                    app.Run();
                    DiagnosticLog.Write("STOP WPF message loop exited");
                }
            }
            return 0;
        }

        private static int RunCheck(AppSettings settings)
        {
            string executable = CodexLocator.FindExecutable(settings.CodexExecutable);
            string home = CodexLocator.FindCodexHome(settings.CodexHome);
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["codexExecutableFound"] = executable != null;
            result["codexExecutable"] = executable;
            result["codexHomeFound"] = Directory.Exists(home);
            result["sessionLogFolderFound"] = Directory.Exists(Path.Combine(home, "sessions"));
            result["wpfRuntime"] = Environment.Version.ToString();
            Console.WriteLine(new JavaScriptSerializer().Serialize(result));
            return executable == null ? 2 : 0;
        }

        private static int RunOnce(AppSettings settings)
        {
            DashboardSnapshot data = new DashboardService(settings).Refresh();
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["success"] = data.IsSuccess;
            result["stale"] = data.IsStale;
            result["error"] = data.ErrorMessage;
            result["todayTokens"] = data.TodayTokens;
            result["todayRequests"] = data.TodayRequests;
            result["usageSource"] = data.UsageSource;
            if (data.Quota != null)
            {
                result["planType"] = data.Quota.PlanType;
                result["resetCreditsAvailable"] = data.Quota.ResetCreditsAvailable;
                result["primary"] = SafeWindow(data.Quota.Primary);
                result["secondary"] = SafeWindow(data.Quota.Secondary);
            }
            Console.WriteLine(new JavaScriptSerializer().Serialize(result));
            return data.IsSuccess ? 0 : 3;
        }

        private static object SafeWindow(QuotaWindowData window)
        {
            if (window == null) return null;
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["label"] = window.Label;
            result["remainingPercent"] = window.RemainingPercent;
            result["resetsAt"] = window.ResetsAtLocal.HasValue
                ? window.ResetsAtLocal.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : null;
            return result;
        }

        private static bool HasArgument(string[] args, string value)
        {
            foreach (string arg in args) if (string.Equals(arg, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool SignalEvent(string name)
        {
            try
            {
                EventWaitHandle handle = EventWaitHandle.OpenExisting(name);
                using (handle) handle.Set();
                return true;
            }
            catch { return false; }
        }
    }
}
