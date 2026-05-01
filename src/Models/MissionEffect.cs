using System;
using System.Collections.Generic;
using System.Globalization;

namespace GTAVTrueCrimesMod.Models
{
    public class MissionEffect
    {
        public string type;
        public string id;
        public Dictionary<string, string> args = new Dictionary<string, string>();

        public string GetString(string key, string fallback)
        {
            if (args == null || !args.ContainsKey(key))
                return fallback;

            return args[key];
        }

        public float GetFloat(string key, float fallback)
        {
            try
            {
                string value = GetString(key, "");

                if (string.IsNullOrEmpty(value))
                    return fallback;

                return float.Parse(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        public int GetInt(string key, int fallback)
        {
            try
            {
                string value = GetString(key, "");

                if (string.IsNullOrEmpty(value))
                    return fallback;

                return int.Parse(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        public bool GetBool(string key, bool fallback)
        {
            string value = GetString(key, "");

            if (string.IsNullOrEmpty(value))
                return fallback;

            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                return false;

            return fallback;
        }
    }
}
