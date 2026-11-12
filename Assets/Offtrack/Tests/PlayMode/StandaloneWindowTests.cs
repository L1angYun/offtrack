using System.Collections;
using NUnit.Framework;
using Offtrack.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Offtrack.Tests.PlayMode
{
    public sealed class StandaloneWindowTests
    {
        [UnityTest]
        public IEnumerator EditorGameViewIsNotResizedByStandalonePolicy()
        {
            int width = Screen.width;
            int height = Screen.height;
            bool fullScreen = Screen.fullScreen;
            StandaloneWindow.Resize(new CutsceneSettings { ResizeStandaloneWindow = true }, 7, 11);
            yield return null;
            yield return null;
            Assert.That(Screen.width, Is.EqualTo(width));
            Assert.That(Screen.height, Is.EqualTo(height));
            Assert.That(Screen.fullScreen, Is.EqualTo(fullScreen));
        }
    }
}
