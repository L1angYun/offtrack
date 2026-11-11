using System;
using System.Collections.Generic;
using UnityEngine;

namespace Offtrack.Runtime.Presentation
{
    public sealed class ScratchPool : FlowView
    {
        [SerializeField] private GameObject[] items = Array.Empty<GameObject>();
        private int[] assignments;
        private FlatShape[][] shapes;
        public IReadOnlyList<GameObject> Items => items;
        public int Capacity => items.Length;
        public int ActiveCount { get; private set; }

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
            shapes = new FlatShape[items.Length][];
            ActiveCount = 0;
            for (var i = 0; i < items.Length; i++)
            {
                assignments[i] = -1;
                shapes[i] = items[i].GetComponentsInChildren<FlatShape>(true);
                // A disabled pool may miss retirement events. Rebuild both the assignment
                // table and its visible objects before refreshing from the current clock.
                items[i].SetActive(false);
            }
        }

        private bool Visible(double noteTime)
        {
            var config = flow.Config;
            var delta = flow.Simulation.Time - noteTime;
            var period = config.BeatsPerRevolution * (60.0 / config.Bpm);
            return delta >= -(period - config.ScratchLookAheadMargin) && delta <= config.ScratchRetireDelay;
        }

        public override void Refresh()
        {
            if (flow == null || flow.Simulation == null || assignments == null) return;
            var sim = flow.Simulation;
            var notes = sim.Notes;
            var idle = sim.State == Core.RunState.Idle;
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
                    var color = note.Hit || note.Passed ? Palette.DarkRed : Palette.Red;
                    foreach (var shape in shapes[slot]) shape.color = color;
                    items[slot].SetActive(true);
                }
            }
            ActiveCount = 0;
            foreach (var item in items) if (item.activeSelf) ActiveCount++;
        }
    }
}
