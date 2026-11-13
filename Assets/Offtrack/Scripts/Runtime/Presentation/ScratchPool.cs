using System;
using System.Collections.Generic;
using Offtrack.Core;
using UnityEngine;

namespace Offtrack.Runtime.Presentation
{
    public sealed class ScratchPool : FlowView
    {
        [SerializeField] private GameObject[] items = Array.Empty<GameObject>();
        private int[] assignments;
        private ScratchVisual[] visuals;
        public IReadOnlyList<GameObject> Items => items;
        public int Capacity => items.Length;
        public int ActiveCount { get; private set; }
        public int HighlightedNoteId { get; private set; } = -1;

        public void Configure(GameFlow value, GameObject[] preallocated)
        {
            items = preallocated;
            Cache();
            Bind(value);
        }

        protected override void OnEnable()
        {
            Cache();
            base.OnEnable();
        }

        private void Cache()
        {
            assignments = new int[items.Length];
            visuals = new ScratchVisual[items.Length];
            ActiveCount = 0;
            for (var i = 0; i < items.Length; i++)
            {
                assignments[i] = -1;
                visuals[i] = items[i].GetComponent<ScratchVisual>();
                // A disabled pool may miss retirement events. Rebuild both the assignment
                // table and its visible objects before refreshing from the current clock.
                items[i].SetActive(false);
            }
        }

        private bool Visible(double noteTime)
        {
            var config = flow.Config;
            var delta = flow.Simulation.Time - noteTime;
            return delta >= -LookAhead(config) && delta <= config.ScratchRetireDelay;
        }

        public static double LookAhead(GameConfig config)
        {
            var period = config.BeatsPerRevolution * (60.0 / config.Bpm);
            return Math.Max(0, Math.Min(period - config.ScratchLookAheadMargin,
                period * config.View.ScratchLookAheadRevolutions));
        }

        /// <summary>The single main-red focus exists only while the record plays (or the needle drops).</summary>
        public static int HighlightFor(GameFlow value) =>
            value.Phase == MachinePhase.Running || value.Phase == MachinePhase.Starting
                ? FindHighlightedNote(value.Simulation, value.Config) : -1;

        public static int FindHighlightedNote(GameSimulation simulation, GameConfig config)
        {
            if (simulation.State == RunState.Idle) return -1;
            var horizon = config.View.ScratchHighlightBeats * 60.0 / config.Bpm;
            for (var id = 0; id < simulation.Notes.Count; id++)
            {
                var note = simulation.Notes[id];
                var window = note.Time < config.WindowSwitchTime ? config.EarlyWindow : config.LateWindow;
                // Keep the same focus through the whole judgment window, including a hit.
                // Damage must not create a presentation reaction in the record itself.
                if (simulation.Time <= note.Time + window && note.Time - simulation.Time <= horizon)
                    return id;
            }
            return -1;
        }

        public override void Refresh()
        {
            if (flow == null || flow.Simulation == null || assignments == null) return;
            var sim = flow.Simulation;
            var notes = sim.Notes;
            var idle = sim.State == Core.RunState.Idle;
            HighlightedNoteId = HighlightFor(flow);
            for (var slot = 0; slot < items.Length; slot++)
            {
                var id = assignments[slot];
                if (idle || id >= notes.Count || (id >= 0 && !Visible(notes[id].Time)))
                {
                    assignments[slot] = -1;
                    items[slot].SetActive(false);
                }
            }
            if (!idle)
            {
                for (var id = 0; id < notes.Count; id++)
                {
                    if (!Visible(notes[id].Time)) continue;
                    var slot = Array.IndexOf(assignments, id);
                    if (slot < 0)
                    {
                        slot = Array.IndexOf(assignments, -1);
                        if (slot < 0) throw new InvalidOperationException("划痕池容量不足，请检查配置和谱面密度。");
                        assignments[slot] = id;
                    }
                    var note = notes[id];
                    var theta = note.Time * Math.PI * 2 / (flow.Config.BeatsPerRevolution * (60.0 / flow.Config.Bpm));
                    var radius = flow.Config.View.DiscRadius * flow.Config.View.LaneRadiusRatios[note.Lane];
                    var rect = (RectTransform)items[slot].transform;
                    rect.anchoredPosition = new Vector2((float)-Math.Sin(theta), (float)Math.Cos(theta)) * radius;
                    rect.localRotation = Quaternion.Euler(0, 0, (float)(theta * 180 / Math.PI) + 90);
                    // Opening scratches grow in while the needle drops (run time is negative then).
                    var bornAt = Math.Max(-flow.PreparationLength, note.Time - LookAhead(flow.Config));
                    var growth = flow.Config.View.ScratchGrowthSeconds <= 0 ? 1 :
                        (float)((flow.RunTime - bornAt) / flow.Config.View.ScratchGrowthSeconds);
                    visuals[slot].Present(id, id == HighlightedNoteId, growth, flow.Config.View);
                    items[slot].SetActive(true);
                }
            }
            ActiveCount = 0;
            foreach (var item in items) if (item.activeSelf) ActiveCount++;
        }
    }
}
