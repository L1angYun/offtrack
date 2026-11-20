using System;
using System.Linq;

namespace Offtrack.Core
{
    public static class ChartFairness
    {
        public static bool EvenBeats(RecordChart chart) => chart.Reversals.All(Even) &&
            chart.Contacts.All(note => Even(note.Time / chart.BeatSeconds) && Even(note.EndTime / chart.BeatSeconds));

        public static bool ClearIntroductions(RecordChart chart, double runnerEnd)
        {
            if (chart.Reversals.Any(reverse => chart.Contacts.Any(note => Math.Abs(note.Time / chart.BeatSeconds - reverse) < 2 - 1e-9)))
                return false;
            if (chart.Hazards.Any(hazard => hazard.kind == HazardKind.RunningScratch) && runnerEnd < 2) return false;
            return Alone(HazardKind.Break) && Alone(HazardKind.RunningScratch);

            bool Alone(HazardKind kind)
            {
                var first = chart.Hazards.Where(hazard => hazard.kind == kind).OrderBy(hazard => hazard.phase).FirstOrDefault();
                if (first == null) return true;
                double end = kind == HazardKind.Break ? first.endPhase : first.phase;
                return chart.Hazards.All(other => other == first || other.phase > end + 2 ||
                    (other.kind == HazardKind.Break ? other.endPhase : other.phase) < first.phase - 2);
            }
        }

        public static bool Traversable(RecordChart chart, RuleConfig rules)
        {
            var breaks = chart.Contacts.Where(note => note.EndTime > note.Time).ToArray();
            double First(ChartNote note) => note.Time - JudgeRules.WindowFor(note.Time, rules);
            double Last(ChartNote note) => note.EndTime + JudgeRules.WindowFor(note.EndTime, rules);
            foreach (var gap in breaks)
            {
                if (rules.LaneCount < 2 || breaks.Count(other => First(other) <= Last(gap) && Last(other) >= First(gap)) > 1)
                    return false;
                if (chart.Contacts.Any(other => other.HazardId != gap.HazardId && other.Lane == gap.Lane &&
                    First(other) <= Last(gap) && Last(other) >= First(gap))) return false;
                var source = chart.Hazards[gap.HazardId];
                if (source.endPhase - source.phase < 2 || source.endPhase - source.phase > 4 ||
                    chart.Hazards.Any(other => other != source && other.phase > source.phase - 2 && other.phase < source.endPhase + 2))
                    return false;
            }
            return chart.Contacts.All(point => chart.Contacts.Where(note => First(note) <= First(point) && Last(note) >= First(point))
                .Select(note => note.Lane).Distinct().Count() < rules.LaneCount);
        }

        public static bool VisibleBeforeContacts(RecordChart chart, double left, double right)
        {
            foreach (var note in chart.Contacts)
            {
                double end = note.Time / chart.BeatSeconds;
                double start = end - 4;
                if (start < -1e-9 || !chart.IsVisible(note.HazardId, Math.Max(0, start), left, right)) return false;
                foreach (double reverse in chart.Reversals)
                    if (reverse >= start && reverse < end && !chart.IsVisible(note.HazardId, reverse, left, right)) return false;
                if (note.Pass > 1)
                {
                    double reverse = chart.Reversals.Last(beat => beat <= end);
                    var previous = chart.Contacts.Last(contact => contact.HazardId == note.HazardId && contact.Pass == note.Pass - 1);
                    if (Math.Abs(reverse - previous.Time / chart.BeatSeconds - (end - reverse)) < 1e-9 &&
                        !chart.IsVisible(note.HazardId, reverse, left, right)) return false;
                }
            }
            return true;
        }

        public static int[] ForcedJumps(RecordChart chart, RuleConfig rules, double lead, int beatsPerSection)
        {
            ChartPlanner.Simulate(rules, chart.Contacts, lead, out var presses);
            var counts = new int[(int)Math.Ceiling(chart.DurationBeats / beatsPerSection - 1e-9)];
            foreach (var press in presses)
            {
                int section = (int)Math.Round(chart.Contacts[press.ContactIndex].Time / chart.BeatSeconds) / beatsPerSection;
                counts[Math.Min(section, counts.Length - 1)]++;
            }
            return counts;
        }

        public static bool PreservesDurability(RecordChart chart, RuleConfig rules, double lead)
        {
            var run = ChartPlanner.Simulate(rules, chart.Contacts, lead, out _);
            return run.Won && run.DamageCount == 0;
        }

        public static bool FailsBeforeHalfWithoutInput(RecordChart chart, RuleConfig rules, out double time)
        {
            var run = ChartPlanner.Simulate(rules, chart.Contacts, null, out _);
            time = run.Time;
            return !run.Won && time < rules.Duration / 2;
        }

        public static bool CoversEveryTrackAfterReversals(RecordChart chart, int laneCount) => chart.Reversals.All(reverse =>
            chart.Contacts.Where(note => WithinReturnWindow(note, chart, reverse)).Select(note => note.Lane).Distinct().Count() == laneCount);

        public static int ReturnedContacts(RecordChart chart, double reverse) => chart.Contacts.Count(note =>
            WithinReturnWindow(note, chart, reverse) && IsReturn(note, chart, reverse));

        public static int[] ForcedReturnJumps(RecordChart chart, RuleConfig rules, double lead)
        {
            ChartPlanner.Simulate(rules, chart.Contacts, lead, out var presses);
            return chart.Reversals.Select(reverse => presses.Count(press =>
            {
                var note = chart.Contacts[press.ContactIndex];
                return WithinReturnWindow(note, chart, reverse) && IsReturn(note, chart, reverse);
            })).ToArray();
        }

        public static int MinimumTrackSwitches(RecordChart chart, RuleConfig rules)
        {
            const int unreachable = int.MaxValue / 2;
            var costs = Enumerable.Range(0, rules.LaneCount).Select(lane => lane == rules.StartLane ? 0 : unreachable).ToArray();
            for (int beat = 2; beat <= chart.DurationBeats + 1e-9; beat += 2)
            {
                double time = beat * chart.BeatSeconds;
                var next = new int[costs.Length];
                for (int lane = 0; lane < costs.Length; lane++)
                    next[lane] = chart.Contacts.Any(note => note.Lane == lane && note.Time <= time && note.EndTime >= time)
                        ? unreachable : Math.Min(costs[lane], Math.Min(lane > 0 ? costs[lane - 1] : unreachable,
                            lane + 1 < costs.Length ? costs[lane + 1] : unreachable) + 1);
                costs = next;
            }
            return costs.Min() >= unreachable ? -1 : costs.Min();
        }

        private static bool WithinReturnWindow(ChartNote note, RecordChart chart, double reverse) =>
            note.Time >= reverse * chart.BeatSeconds && note.Time <= (reverse + 8) * chart.BeatSeconds;

        private static bool IsReturn(ChartNote note, RecordChart chart, double reverse) => note.Pass > 1 &&
            chart.Contacts.Any(previous => previous.HazardId == note.HazardId && previous.Pass == note.Pass - 1 &&
                Math.Abs(previous.Time + note.Time - 2 * reverse * chart.BeatSeconds) < 1e-9);

        private static bool Even(double beat) => Math.Abs(beat - 2 * Math.Round(beat / 2)) < 1e-9;
    }
}
