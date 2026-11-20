using System.IO;
using System.Linq;
using System.Text;
using Offtrack.Core;
using Offtrack.Runtime;
using UnityEditor;
using UnityEngine;

namespace Offtrack.Editor
{
    internal static class V3ChartBuilder
    {
        internal static void Generate(GameConfig config, string directory)
        {
            if (config.Levels.Length != 3) config.Levels = new LevelChart[3];
            for (int index = 0; index < config.Levels.Length; index++)
            {
                string stem = $"{directory}/side-a.l{index + 1}";
                var source = JsonUtility.FromJson<RecordChartSource>(File.ReadAllText(stem + ".beats.json", Encoding.UTF8));
                var record = new RecordChart(source, config.ToRules());
                var generated = new GeneratedChart { source = source, contacts = record.Contacts.Select(note => new GeneratedContact(note)).ToArray() };
                string path = stem + ".json";
                File.WriteAllText(path, JsonUtility.ToJson(generated, true) + "\n", new UTF8Encoding(false));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var level = config.Levels[index] ??= new LevelChart();
                if (record.Reversals.Count == 0)
                {
                    level.LeftBeats = config.BeatsPerRevolution * config.View.ScratchLookAheadRevolutions;
                    level.RightBeats = config.ScratchRetireDelay / record.BeatSeconds;
                }
                level.Chart = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                level.Guide = source.guide;
                level.Fragment = source.fragment;
            }
            EditorUtility.SetDirty(config);
        }
    }
}
