using System;
using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    public static class FontProvider
    {
        private static readonly string[] PreferredNames =
            { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC" };
        private static Font cached;
        private static Font serif;
        public static string SelectedName { get; private set; }
        public static string SelectedSerifName { get; private set; }

        public static Font Fallback => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Font Get()
        {
            if (cached != null) return cached;
            var installed = Font.GetOSInstalledFontNames();
            foreach (var preferred in PreferredNames)
            {
                if (Array.Exists(installed, name => string.Equals(name, preferred, StringComparison.OrdinalIgnoreCase)))
                {
                    cached = Font.CreateDynamicFontFromOSFont(preferred, 32);
                    SelectedName = preferred;
                    break;
                }
            }
            if (cached == null)
            {
                cached = Fallback;
                SelectedName = "LegacyRuntime.ttf";
            }
            return cached;
        }

        public static void Apply(Text text) => text.font = Get();

        public static Font GetSerif()
        {
            if (serif != null) return serif;
            var installed = Font.GetOSInstalledFontNames();
            // Match the reference canvas's preference order.
            foreach (var preferred in new[] { "Georgia", "Times New Roman", "Songti SC" })
            {
                if (!Array.Exists(installed, name => string.Equals(name, preferred, StringComparison.OrdinalIgnoreCase))) continue;
                serif = Font.CreateDynamicFontFromOSFont(preferred, 46);
                SelectedSerifName = preferred;
                return serif;
            }
            serif = Get();
            SelectedSerifName = SelectedName;
            return serif;
        }
    }
}
