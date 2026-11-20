using System;
using System.Linq;
using Offtrack.Runtime.Presentation;
using UnityEngine;

namespace Offtrack.Tests.PlayMode
{
    /// <summary>
    /// Strict palette checks cover the unchanged base artwork. The newly requested translucent
    /// damage wash is captured and pixel-tested separately by DamageFeedbackTests.
    /// Hide only that wash without stopping its timer, then restore it after the capture.
    /// </summary>
    internal sealed class BaseArtworkCapture : IDisposable
    {
        private readonly CanvasGroup[] groups;
        private readonly float[] opacity;

        public BaseArtworkCapture()
        {
            groups = UnityEngine.Object.FindObjectsByType<DamageVignette>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(graphic => graphic.GetComponent<CanvasGroup>()).Where(group => group != null).ToArray();
            opacity = groups.Select(group => group.alpha).ToArray();
            foreach (var group in groups) group.alpha = 0;
        }

        public void Dispose()
        {
            for (int i = 0; i < groups.Length; i++) if (groups[i] != null) groups[i].alpha = opacity[i];
        }
    }
}
