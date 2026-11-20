using System.Collections;
using NUnit.Framework;
using Offtrack.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Offtrack.Tests.PlayMode
{
    public sealed class StandaloneWindowTests
    {
#if UNITY_EDITOR
        [Test]
        public void SendsExactlyOneResolutionRequestOnlyWhenWindowed()
        {
            int actualCalls = 0;
            bool fullscreen = true;
            var settings = new CutsceneSettings();
            StandaloneWindow.SetTestWindowBackend(() => fullscreen, (width, height) =>
            {
                Assert.That(width, Is.EqualTo(settings.JourneyWindowWidth));
                Assert.That(height, Is.EqualTo(settings.JourneyWindowHeight));
                actualCalls++;
            });
            try
            {
                StandaloneWindow.ApplyJourneyWindowOnce(settings);
                Assert.That(actualCalls, Is.Zero);
                Assert.That(StandaloneWindow.RequestCount, Is.Zero);
                fullscreen = false;
                StandaloneWindow.ApplyJourneyWindowOnce(settings);
                StandaloneWindow.ApplyJourneyWindowOnce(settings);
                Assert.That(actualCalls, Is.EqualTo(1));
                Assert.That(StandaloneWindow.RequestCount, Is.EqualTo(actualCalls));
            }
            finally { StandaloneWindow.SetTestWindowBackend(null, null); }
        }
#endif
        [UnityTest]
        public IEnumerator EditorGameViewIsNotResizedByStandalonePolicy()
        {
            int width = Screen.width;
            int height = Screen.height;
            bool fullScreen = Screen.fullScreen;
            StandaloneWindow.ApplyJourneyWindowOnce(new CutsceneSettings { ResizeStandaloneWindow = true, JourneyWindowWidth = 7, JourneyWindowHeight = 11 });
            yield return null;
            yield return null;
            Assert.That(Screen.width, Is.EqualTo(width));
            Assert.That(Screen.height, Is.EqualTo(height));
            Assert.That(Screen.fullScreen, Is.EqualTo(fullScreen));
        }
    }
}
