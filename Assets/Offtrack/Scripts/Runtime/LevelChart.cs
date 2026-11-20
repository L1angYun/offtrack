using System;
using System.Collections.Generic;
using System.Linq;
using Offtrack.Core;
using UnityEngine;

namespace Offtrack.Runtime
{
    [Serializable]
    public sealed class LevelChart
    {
        public TextAsset Chart;
        [Tooltip("Optional hazards and reversals for playback beyond the authored chart.")]
        public TextAsset Continuation;
        [Tooltip("Music for this level. Its imported clip length sets the playable duration.")]
        public AudioClip Music;
        [Tooltip("Repeat the authored hazard pattern every N beats. Zero keeps the chart as authored.")]
        [Min(0)] public int RepeatBeats;
        public string Guide;
        public string Fragment;
        public double LeftBeats = 10;
        public double RightBeats = 6;
        public double RunnerStart = 3;
        public double RunnerEnd = 2;
        public double ArrowLead = 2;
        public double AudioWarningLead = 1;

        public RecordChart Load(RuleConfig rules) => Load(rules, rules);

        internal RecordChart Load(RuleConfig authoredRules, RuleConfig playRules)
        {
            var data = JsonUtility.FromJson<GeneratedChart>(Chart.text);
            // The generated table describes the authored side, before the music changes its length.
            var record = new RecordChart(data.source, authoredRules);
            if (data.contacts == null || data.contacts.Length != record.Contacts.Count)
                throw new InvalidOperationException("The generated contact table is out of date.");
            for (int index = 0; index < data.contacts.Length; index++)
            {
                var expected = record.Contacts[index];
                var contact = data.contacts[index];
                if (Math.Abs(expected.Time - contact.t) > 1e-9 || Math.Abs(expected.EndTime - contact.end) > 1e-9 ||
                    expected.Lane != contact.lane || expected.HazardId != contact.hazard || expected.Pass != contact.pass)
                    throw new InvalidOperationException("The generated contact table does not match the record.");
            }
            if (playRules.Duration == authoredRules.Duration) return record;

            double durationBeats = playRules.Duration / record.BeatSeconds;
            if (RepeatBeats != 0)
                data.source.hazards = RepeatHazards(record, durationBeats);
            if (Continuation != null && playRules.Duration > authoredRules.Duration)
                AppendContinuation(data.source, durationBeats);
            // A longer final segment can bring existing hazards back under the needle.
            return new RecordChart(data.source, playRules);
        }

        private void AppendContinuation(RecordChartSource source, double durationBeats)
        {
            var continuation = JsonUtility.FromJson<RecordChartSource>(Continuation.text);
            source.reversals = (source.reversals ?? Array.Empty<double>())
                .Concat(continuation.reversals.Where(beat => beat < durationBeats)).ToArray();
            source.hazards = source.hazards
                .Concat(continuation.hazards.Where(hazard => hazard.phase <= durationBeats &&
                    (hazard.kind != HazardKind.Break || hazard.endPhase <= durationBeats))).ToArray();
        }

        private RecordHazard[] RepeatHazards(RecordChart record, double durationBeats)
        {
            if (RepeatBeats < 0 || record.Reversals.Count > 0)
                throw new InvalidOperationException("Only forward charts can repeat their hazards.");
            foreach (var hazard in record.Hazards)
            {
                if (hazard.phase >= RepeatBeats || (hazard.kind == HazardKind.Break && hazard.endPhase > RepeatBeats))
                    throw new InvalidOperationException("Hazards must fit within the repeat range.");
            }

            var hazards = new List<RecordHazard>();
            for (double offset = 0; offset < durationBeats; offset += RepeatBeats)
            {
                foreach (var hazard in record.Hazards)
                {
                    double phase = hazard.phase + offset;
                    bool isBreak = hazard.kind == HazardKind.Break;
                    double endPhase = isBreak ? hazard.endPhase + offset : hazard.endPhase;
                    if (phase > durationBeats || (isBreak && endPhase > durationBeats)) continue;
                    hazards.Add(new RecordHazard
                    {
                        kind = hazard.kind,
                        phase = phase,
                        endPhase = endPhase,
                        lane = hazard.lane,
                        targetLane = hazard.targetLane
                    });
                }
            }
            return hazards.ToArray();
        }
    }

    [Serializable]
    public sealed class GeneratedChart
    {
        public RecordChartSource source;
        public GeneratedContact[] contacts;
    }

    [Serializable]
    public struct GeneratedContact
    {
        public double t, end;
        public int lane, hazard, pass;

        public GeneratedContact(ChartNote note)
        {
            t = note.Time;
            end = note.EndTime;
            lane = note.Lane;
            hazard = note.HazardId;
            pass = note.Pass;
        }
    }
}
