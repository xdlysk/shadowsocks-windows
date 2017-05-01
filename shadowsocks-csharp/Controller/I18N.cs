using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Shadowsocks.Properties;

namespace Shadowsocks.Controller
{
    public static class I18N
    {
        private static readonly Dictionary<string, string> _strings = new Dictionary<string, string>();

        static I18N()
        {
            var name = CultureInfo.CurrentCulture.EnglishName;
            if (name.StartsWith("Chinese", StringComparison.OrdinalIgnoreCase))
                Init(name.Contains("Traditional")
                    ? Resources.zh_TW
                    : Resources.zh_CN);
            else if (name.StartsWith("Japan", StringComparison.OrdinalIgnoreCase))
                Init(Resources.ja);
        }

        private static void Init(string res)
        {
            using (var sr = new StringReader(res))
            {
                foreach (var line in sr.NonWhiteSpaceLines())
                {
                    if (line[0] == '#')
                        continue;

                    var pos = line.IndexOf('=');
                    if (pos < 1)
                        continue;
                    _strings[line.Substring(0, pos)] = line.Substring(pos + 1);
                }
            }
        }

        public static string GetString(string key)
        {
            return _strings.ContainsKey(key)
                ? _strings[key]
                : key;
        }
    }
}