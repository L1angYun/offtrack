using System;
using UnityEngine;
using UnityEngine.UI;

namespace Offtrack.Runtime.Presentation
{
    public sealed class TrackIndicatorView : FlowView
    {
        [SerializeField] private FlatShape[] contacts = Array.Empty<FlatShape>();
        [SerializeField] private Text[] numbers = Array.Empty<Text>();
        [SerializeField] private FlatShape[] bands = Array.Empty<FlatShape>();
        [SerializeField] private FlatShape[] approach = Array.Empty<FlatShape>();
        public int HighlightedLane { get; private set; } = -1;

        public void Configure(GameFlow value, FlatShape[] contactMarks, Text[] contactNumbers,
            FlatShape[] laneBands, FlatShape[] approachArcs)
        {
            contacts = contactMarks; numbers = contactNumbers; bands = laneBands; approach = approachArcs;
            Bind(value);
        }

        public override void Refresh()
        {
            if (flow == null || flow.Simulation == null) return;
            var next = ScratchPool.HighlightFor(flow);
            HighlightedLane = next < 0 ? -1 : flow.Simulation.Notes[next].Lane;
            for (var lane = 0; lane < contacts.Length; lane++)
            {
                var current = lane == flow.Simulation.Lane;
                contacts[lane].color = numbers[lane].color = current ? Palette.Bone : Palette.DarkRed;
                bands[lane].color = current ? Palette.DarkRed40 : Palette.DarkRed15;
                approach[lane].gameObject.SetActive(lane == HighlightedLane);
            }
        }
    }
}
