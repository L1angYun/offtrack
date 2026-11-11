namespace Offtrack.Core
{
    public static class JudgeRules
    {
        public static double WindowFor(double scratchTime, RuleConfig rules) =>
            scratchTime < rules.WindowSwitchTime ? rules.EarlyWindow : rules.LateWindow;

        public static string ClassifyLanding(double time, double lastLanding, double earlyLandingThreshold) =>
            time - lastLanding < earlyLandingThreshold ? "landedTooEarly" : "jumpedTooLate";
    }
}
