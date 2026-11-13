using System;
using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    public static class FontProvider
    {
        private static readonly string[] PreferredNames =
            { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC" };
        // Machine print (timer digits, track numbers, label and plinth print). System fonts only:
        // nothing is bundled or downloaded. Later names are glyph fallbacks (e.g. ⅓, Chinese).
        private static readonly string[] MonospaceNames = { "Consolas", "Cascadia Mono", "Menlo", "DejaVu Sans Mono" };
        private static Font cached;
        private static Font serif;
        private static Font mono;
        public static string SelectedName { get; private set; }
        public static string SelectedSerifName { get; private set; }
        public static string SelectedMonospaceName { get; private set; }
        public static bool HasMonospace => GetMonospace() != Get();

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
#if !UNITY_EDITOR
            Debug.Log("[OFFTRACK] UI font: " + SelectedName);
#endif
            return cached;
        }

        public static void Apply(Text text) => text.font = Get();
        public static void Apply(Text text, bool monospace) => text.font = monospace ? GetMonospace() : Get();

        public static Font GetMonospace()
        {
            if (mono != null) return mono;
            var body = Get();
            var installed = Font.GetOSInstalledFontNames();
            foreach (var preferred in MonospaceNames)
            {
                if (!Array.Exists(installed, name => string.Equals(name, preferred, StringComparison.OrdinalIgnoreCase))) continue;
                var names = body == Fallback ? new[] { preferred } : new[] { preferred, SelectedName };
                mono = Font.CreateDynamicFontFromOSFont(names, 32);
                SelectedMonospaceName = preferred;
                break;
            }
            if (mono == null)
            {
                mono = body;
                SelectedMonospaceName = SelectedName;
            }
#if !UNITY_EDITOR
            Debug.Log("[OFFTRACK] Machine print font: " + SelectedMonospaceName);
#endif
            return mono;
        }

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
