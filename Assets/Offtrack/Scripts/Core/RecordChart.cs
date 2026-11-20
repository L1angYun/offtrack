using System;
using System.Collections.Generic;
using System.Linq;

namespace Offtrack.Core
{
    public enum HazardKind { Scratch, RunningScratch, Break }

    [Serializable]
    public sealed class RecordHazard
    {
        public HazardKind kind;
        public double phase;
        public double endPhase;
        public int lane;
        public int targetLane;

        public double RadialLane(double delta, double motionStart, double motionEnd)
        {
            if (kind != HazardKind.RunningScratch) return lane;
            double amount = Math.Max(0, Math.Min(1, (motionStart - delta) / (motionStart - motionEnd)));
            amount *= amount * (3 - 2 * amount);
            return lane + (targetLane - lane) * amount;
        }
    }

    [Serializable]
    public sealed class RecordChartSource
    {
        public string guide;
        public string fragment;
        public RecordHazard[] hazards;
        public double[] reversals;
    }

    public sealed class RecordChart
    {
        private readonly double[] reversals;
        private readonly double[] phases;
        public readonly IReadOnlyList<RecordHazard> Hazards;
        public readonly IReadOnlyList<double> Reversals;
        public readonly IReadOnlyList<ChartNote> Contacts;
        public readonly double BeatSeconds;
        public readonly double DurationBeats;

        public RecordChart(RecordChartSource source, RuleConfig rules)
        {
            if (source == null || source.hazards == null) throw new ArgumentException("Missing hazard source.", nameof(source));
            BeatSeconds = 60 / rules.Bpm;
            DurationBeats = rules.Duration / BeatSeconds;
            reversals = (double[])(source.reversals ?? Array.Empty<double>()).Clone();
            Reversals = Array.AsReadOnly(reversals);
            phases = new double[reversals.Length];
            double start = 0;
            double phase = 0;
            for (int index = 0; index < reversals.Length; index++)
            {
                double reverse = reversals[index];
                if (!(reverse > start && reverse < DurationBeats)) throw new ArgumentException("Reversals must increase inside the run.");
                phase += (reverse - start) * Direction(index);
                phases[index] = phase;
                start = reverse;
            }
            Hazards = Array.AsReadOnly((RecordHazard[])source.hazards.Clone());
            Contacts = Array.AsReadOnly(Expand(rules.LaneCount));
        }

        private static int Direction(int segment) => segment % 2 == 0 ? 1 : -1;
        private int Segment(double beat)
        {
            int index = Array.BinarySearch(reversals, beat);
            return index >= 0 ? index + 1 : ~index;
        }

        public int DirectionAt(double beat) => Direction(Segment(beat));
        public int LabelDirection(double beat, double lead) => DirectionAt(Math.Min(DurationBeats, beat + lead));

        public bool CrossedReversal(double from, double to, double lead) =>
            Reversals.Any(reverse => from < reverse - lead && to >= reverse - lead);

        public double PhaseAt(double beat)
        {
            int segment = Segment(beat);
            return segment == 0 ? beat : phases[segment - 1] + (beat - reversals[segment - 1]) * Direction(segment);
        }

        public bool IsVisible(int id, double beat, double lookAhead, double lookBehind)
        {
            var hazard = Hazards[id];
            double phase = PhaseAt(beat);
            double tail = hazard.kind == HazardKind.Break ? hazard.endPhase : hazard.phase;
            return tail - phase >= -lookBehind && hazard.phase - phase < lookAhead;
        }

        private ChartNote[] Expand(int lanes)
        {
            var result = new List<ChartNote>();
            for (int id = 0; id < Hazards.Count; id++)
            {
                var hazard = Hazards[id];
                if (hazard == null || !Enum.IsDefined(typeof(HazardKind), hazard.kind) ||
                    !(hazard.phase >= 0 && hazard.phase <= DurationBeats) || hazard.lane < 0 || hazard.lane >= lanes)
                    throw new ArgumentException($"Invalid hazard {id}.");
                if (hazard.kind == HazardKind.RunningScratch && (Math.Abs(hazard.lane - hazard.targetLane) != 1 ||
                    hazard.targetLane < 0 || hazard.targetLane >= lanes)) throw new ArgumentException($"Invalid runner {id}.");
                bool gap = hazard.kind == HazardKind.Break;
                if (gap && !(hazard.endPhase > hazard.phase && hazard.endPhase <= DurationBeats))
                    throw new ArgumentException($"Invalid break {id}.");
                int pass = 0;
                for (int segment = 0; segment <= reversals.Length; segment++)
                {
                    double start = segment == 0 ? 0 : reversals[segment - 1];
                    double end = segment == reversals.Length ? DurationBeats : reversals[segment];
                    double phase = segment == 0 ? 0 : phases[segment - 1];
                    int direction = Direction(segment);
                    double beat = start + (hazard.phase - phase) * direction;
                    int lane = hazard.kind == HazardKind.RunningScratch ? hazard.targetLane : hazard.lane;
                    double endBeat = beat;
                    if (gap)
                    {
                        double otherEdge = start + (hazard.endPhase - phase) * direction;
                        endBeat = Math.Min(end, Math.Max(beat, otherEdge));
                        beat = Math.Max(start, Math.Min(beat, otherEdge));
                        if (beat >= endBeat) continue;
                        if (pass > 0 && result[result.Count - 1].EndTime == beat * BeatSeconds)
                        {
                            // A reversal inside a gap does not close the gap under the needle.
                            var previous = result[result.Count - 1];
                            result[result.Count - 1] = new ChartNote(previous.Time, lane, id, pass, endBeat * BeatSeconds);
                            continue;
                        }
                    }
                    else if (beat < start || beat > end || (segment > 0 && beat == start)) continue;
                    result.Add(new ChartNote(beat * BeatSeconds, lane, id, ++pass, endBeat * BeatSeconds));
                }
            }
            return result.OrderBy(note => note.Time).ThenBy(note => note.HazardId).ToArray();
        }
    }
}
