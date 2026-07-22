using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodexQuotaPet
{
    internal static class JsonValue
    {
        public static Dictionary<string, object> Object(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static Dictionary<string, object> Child(Dictionary<string, object> map, string key)
        {
            object value;
            return map != null && map.TryGetValue(key, out value) ? Object(value) : null;
        }

        public static object[] Array(Dictionary<string, object> map, string key)
        {
            object value;
            return map != null && map.TryGetValue(key, out value) ? value as object[] : null;
        }

        public static string String(Dictionary<string, object> map, string key)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value) || value == null) return null;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static long? Long(Dictionary<string, object> map, string key)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value) || value == null) return null;
            long parsed;
            return long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? (long?)parsed : null;
        }

        public static int? Int(Dictionary<string, object> map, string key)
        {
            long? value = Long(map, key);
            if (!value.HasValue) return null;
            return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, value.Value));
        }

        public static double? Double(Dictionary<string, object> map, string key)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value) || value == null) return null;
            double parsed;
            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? (double?)parsed : null;
        }

        public static bool? Bool(Dictionary<string, object> map, string key)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value) || value == null) return null;
            bool parsed;
            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) ? (bool?)parsed : null;
        }
    }
}
