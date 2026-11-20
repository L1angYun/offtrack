using System;
using Offtrack.Core;
using UnityEngine;

namespace Offtrack.Runtime
{
    [Serializable]
    public sealed class LevelChart
    {
        public TextAsset Chart;
        [Tooltip("Optional music for this level. When empty, the original side-A track is used.")]
        public AudioClip Music;
        public string Guide;
        public string Fragment;
        public double LeftBeats = 10;
        public double RightBeats = 6;
        public double RunnerStart = 3;
        public double RunnerEnd = 2;
        public double ArrowLead = 2;
        public double AudioWarningLead = 1;

        public RecordChart Load(RuleConfig rules)
        {
            var data = JsonUtility.FromJson<GeneratedChart>(Chart.text);
            var record = new RecordChart(data.source, rules);
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
            return record;
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
