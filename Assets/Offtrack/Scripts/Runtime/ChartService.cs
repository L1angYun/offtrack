using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Offtrack.Core;
using UnityEngine;

namespace Offtrack.Runtime
{
    public static class ChartService
    {
        // This deliberately recognizes only the documented, non-nested {t, lane} schema.
        // JsonUtility silently supplies zero for missing fields, making a typo a playable note.
        private const string Whitespace = @"[ \t\r\n]*";
        private const string Separator = Whitespace + "," + Whitespace;
        private const string Number = @"-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?";
        private const string TimeField = "\"t\"" + Whitespace + ":" + Whitespace + "(?<time>" + Number + ")";
        private const string LaneField = "\"lane\"" + Whitespace + ":" + Whitespace + @"(?<lane>-?(?:0|[1-9][0-9]*))";
        private const string Note = @"\{" + Whitespace + "(?:" + TimeField + Separator + LaneField
            + "|" + LaneField + Separator + TimeField + ")" + Whitespace + @"\}";
        private static readonly Regex ChartArray = new Regex(@"\A" + Whitespace + @"\[" + Whitespace
            + "(?:" + Note + "(?:" + Separator + Note + ")*)?" + Whitespace + @"\]" + Whitespace + @"\z",
            RegexOptions.CultureInvariant);

        public static ChartNote[] Load(TextAsset chart, int laneCount)
        {
            if (chart == null) throw new ArgumentNullException(nameof(chart));
            return Parse(chart.text, laneCount);
        }

        public static ChartNote[] Parse(string json, int laneCount)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Chart JSON is empty.", nameof(json));
            Match parsed = ChartArray.Match(json);
            if (!parsed.Success)
                throw new ArgumentException("Chart JSON must be an array of notes with exactly one numeric t and one integer lane.", nameof(json));

            CaptureCollection times = parsed.Groups["time"].Captures;
            CaptureCollection lanes = parsed.Groups["lane"].Captures;
            var notes = new ChartNote[times.Count];
            for (int i = 0; i < notes.Length; i++)
            {
                if (!double.TryParse(times[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double time)
                    || !int.TryParse(lanes[i].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int lane))
                    throw new ArgumentException("Chart note " + i + " contains an out-of-range number.", nameof(json));
                notes[i] = new ChartNote(time, lane);
            }
            ChartTools.Validate(notes, laneCount);
            return notes;
        }

        public static string ToJson(IReadOnlyList<ChartNote> chart)
        {
            if (chart == null) throw new ArgumentNullException(nameof(chart));
            var json = new StringBuilder("[\n");
            for (int i = 0; i < chart.Count; i++)
            {
                if (i > 0) json.Append(",\n");
                json.Append("  {\"t\": ");
                json.Append(chart[i].Time.ToString("0.000000################", CultureInfo.InvariantCulture));
                json.Append(", \"lane\": ");
                json.Append(chart[i].Lane.ToString(CultureInfo.InvariantCulture));
                json.Append('}');
            }
            return json.Append("\n]\n").ToString();
        }
    }
}
