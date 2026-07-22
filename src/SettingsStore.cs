using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CodexQuotaPet
{
    internal sealed class AppSettings
    {
        public int RefreshIntervalSeconds { get; set; }
        public int WarningThreshold { get; set; }
        public int CriticalThreshold { get; set; }
        public bool DetailedMode { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool LowQuotaNotification { get; set; }
        public bool StartWithWindows { get; set; }
        public bool SnapToScreen { get; set; }
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public double SimpleWidth { get; set; }
        public double SimpleHeight { get; set; }
        public double DetailWidth { get; set; }
        public double DetailHeight { get; set; }
        public string CodexExecutable { get; set; }
        public string CodexHome { get; set; }

        public AppSettings()
        {
            RefreshIntervalSeconds = 180;
            WarningThreshold = 30;
            CriticalThreshold = 15;
            DetailedMode = false;
            AlwaysOnTop = true;
            LowQuotaNotification = true;
            StartWithWindows = false;
            SnapToScreen = true;
            SimpleWidth = 84;
            SimpleHeight = 84;
            DetailWidth = 590;
            DetailHeight = 760;
        }

        public void Normalize()
        {
            RefreshIntervalSeconds = Math.Max(30, Math.Min(3600, RefreshIntervalSeconds));
            CriticalThreshold = Math.Max(1, Math.Min(50, CriticalThreshold));
            WarningThreshold = Math.Max(CriticalThreshold + 1, Math.Min(80, WarningThreshold));
            SimpleWidth = Math.Max(72, Math.Min(220, SimpleWidth));
            SimpleHeight = Math.Max(72, Math.Min(220, SimpleHeight));
            DetailWidth = Math.Max(520, Math.Min(1200, DetailWidth));
            DetailHeight = Math.Max(560, Math.Min(1000, DetailHeight));
            if (string.IsNullOrWhiteSpace(CodexExecutable)) CodexExecutable = null;
            if (string.IsNullOrWhiteSpace(CodexHome)) CodexHome = null;
        }
    }

    internal sealed class SettingsStore
    {
        private readonly string _path;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public SettingsStore(string baseDirectory)
        {
            _path = Path.Combine(baseDirectory, "settings.json");
        }

        public AppSettings Load()
        {
            AppSettings settings = new AppSettings();
            if (!File.Exists(_path)) return settings;

            try
            {
                Dictionary<string, object> map = _json.DeserializeObject(File.ReadAllText(_path)) as Dictionary<string, object>;
                if (map == null) return settings;
                settings.RefreshIntervalSeconds = ReadInt(map, "refreshIntervalSeconds", settings.RefreshIntervalSeconds);
                settings.WarningThreshold = ReadInt(map, "warningThreshold", settings.WarningThreshold);
                settings.CriticalThreshold = ReadInt(map, "criticalThreshold", settings.CriticalThreshold);
                settings.DetailedMode = ReadBool(map, "detailedMode", settings.DetailedMode);
                settings.AlwaysOnTop = ReadBool(map, "alwaysOnTop", settings.AlwaysOnTop);
                settings.LowQuotaNotification = ReadBool(map, "lowQuotaNotification", settings.LowQuotaNotification);
                settings.StartWithWindows = ReadBool(map, "startWithWindows", settings.StartWithWindows);
                settings.SnapToScreen = ReadBool(map, "snapToScreen", settings.SnapToScreen);
                settings.WindowLeft = ReadNullableDouble(map, "windowLeft");
                settings.WindowTop = ReadNullableDouble(map, "windowTop");
                settings.SimpleWidth = ReadDouble(map, "simpleWidth", settings.SimpleWidth);
                settings.SimpleHeight = ReadDouble(map, "simpleHeight", settings.SimpleHeight);
                settings.DetailWidth = ReadDouble(map, "detailWidth", settings.DetailWidth);
                settings.DetailHeight = ReadDouble(map, "detailHeight", settings.DetailHeight);
                settings.CodexExecutable = ReadString(map, "codexExecutable");
                settings.CodexHome = ReadString(map, "codexHome");
            }
            catch
            {
                // Invalid local settings must never prevent the monitor from starting.
            }
            settings.Normalize();
            return settings;
        }

        public void Save(AppSettings settings)
        {
            settings.Normalize();
            Dictionary<string, object> map = new Dictionary<string, object>();
            map["refreshIntervalSeconds"] = settings.RefreshIntervalSeconds;
            map["warningThreshold"] = settings.WarningThreshold;
            map["criticalThreshold"] = settings.CriticalThreshold;
            map["detailedMode"] = settings.DetailedMode;
            map["alwaysOnTop"] = settings.AlwaysOnTop;
            map["lowQuotaNotification"] = settings.LowQuotaNotification;
            map["startWithWindows"] = settings.StartWithWindows;
            map["snapToScreen"] = settings.SnapToScreen;
            map["windowLeft"] = settings.WindowLeft.HasValue ? (object)settings.WindowLeft.Value : null;
            map["windowTop"] = settings.WindowTop.HasValue ? (object)settings.WindowTop.Value : null;
            map["simpleWidth"] = settings.SimpleWidth;
            map["simpleHeight"] = settings.SimpleHeight;
            map["detailWidth"] = settings.DetailWidth;
            map["detailHeight"] = settings.DetailHeight;
            map["codexExecutable"] = settings.CodexExecutable;
            map["codexHome"] = settings.CodexHome;
            File.WriteAllText(_path, _json.Serialize(map));
        }

        public void ApplyStartupSetting(bool enabled, string executablePath)
        {
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKey, true))
            {
                if (key == null) return;
                if (enabled) key.SetValue("CodexQuotaPet", "\"" + executablePath + "\"", RegistryValueKind.String);
                else key.DeleteValue("CodexQuotaPet", false);
            }
        }

        private static int ReadInt(Dictionary<string, object> map, string key, int fallback)
        {
            object value;
            if (!map.TryGetValue(key, out value) || value == null) return fallback;
            int parsed;
            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static bool ReadBool(Dictionary<string, object> map, string key, bool fallback)
        {
            object value;
            if (!map.TryGetValue(key, out value) || value == null) return fallback;
            bool parsed;
            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) ? parsed : fallback;
        }

        private static double? ReadNullableDouble(Dictionary<string, object> map, string key)
        {
            object value;
            if (!map.TryGetValue(key, out value) || value == null) return null;
            double parsed;
            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? (double?)parsed : null;
        }

        private static double ReadDouble(Dictionary<string, object> map, string key, double fallback)
        {
            double? parsed = ReadNullableDouble(map, key);
            return parsed.HasValue ? parsed.Value : fallback;
        }

        private static string ReadString(Dictionary<string, object> map, string key)
        {
            object value;
            if (!map.TryGetValue(key, out value) || value == null) return null;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
