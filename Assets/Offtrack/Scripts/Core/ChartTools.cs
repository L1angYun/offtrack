using System;
using System.Collections.Generic;

namespace Offtrack.Core
{
    public readonly struct ChartNote
    {
        public ChartNote(double time, int lane) { Time = time; Lane = lane; }
        public double Time { get; }
        public int Lane { get; }
    }

    public readonly struct BeatNote
    {
        public BeatNote(double beat, int lane) { Beat = beat; Lane = lane; }
        public double Beat { get; }
        public int Lane { get; }
    }

    public static class ChartTools
    {
        public static IReadOnlyList<ChartNote> FromBeats(double bpm, IReadOnlyList<BeatNote> beats, int laneCount)
        {
            if (double.IsNaN(bpm) || double.IsInfinity(bpm) || bpm <= 0)
                throw new ArgumentOutOfRangeException(nameof(bpm), "BPM must be finite and positive.");
            if (beats == null) throw new ArgumentNullException(nameof(beats));
            double secondsPerBeat = 60 / bpm;
            var chart = new ChartNote[beats.Count];
            for (int i = 0; i < chart.Length; i++)
                chart[i] = new ChartNote(beats[i].Beat * secondsPerBeat, beats[i].Lane);
            Validate(chart, laneCount);
            return Array.AsReadOnly(chart);
        }

        public static void Validate(IReadOnlyList<ChartNote> chart, int laneCount)
        {
            if (chart == null) throw new ArgumentNullException(nameof(chart));
            if (laneCount <= 0) throw new ArgumentOutOfRangeException(nameof(laneCount));
            double previousTime = 0;
            for (int i = 0; i < chart.Count; i++)
            {
                var note = chart[i];
                if (double.IsNaN(note.Time) || double.IsInfinity(note.Time) || note.Time < 0)
                    throw new ArgumentException("Scratch " + i + " has an invalid time.", nameof(chart));
                if (note.Lane < 0 || note.Lane >= laneCount)
                    throw new ArgumentException("Scratch " + i + " has an out-of-range lane.", nameof(chart));
                if (i > 0 && note.Time < previousTime)
                    throw new ArgumentException("Scratch times must be in ascending order.", nameof(chart));
                previousTime = note.Time;
            }
        }
    }
}
