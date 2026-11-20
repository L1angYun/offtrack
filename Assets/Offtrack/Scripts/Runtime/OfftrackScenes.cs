using System;
using System.Collections.Generic;

namespace Offtrack.Runtime
{
    public static class OfftrackScenes
    {
        public const string Title = "Title";
        public const string CutsceneA = "CutsceneA";
        public const string Main = "Main";
        public const string CutsceneC = "CutsceneC";
        public const string CutsceneB = "CutsceneB";
        public static IReadOnlyList<string> Journey { get; } = Array.AsReadOnly(new[] { Title, CutsceneA, Main, CutsceneC, CutsceneB });
        public static string Path(string scene) => $"Assets/Scenes/{scene}.unity";
    }
}
