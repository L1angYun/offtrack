using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Offtrack.Core;

namespace Offtrack.Tests.EditMode
{
    public sealed class V3SkipTests
    {
        [Test]
        public void EachSkipPressLandsOnTheNextShot()
        {
            var score = new PortraitScore();
            var timing = new CutsceneTiming();
            Assert.That(score.Chapters(StoryScene.Opening, timing), Is.EqualTo(new[] { 3.2, 6, 13 }));
            Assert.That(score.Chapters(StoryScene.Montage, timing), Is.EqualTo(new[] { 2.0, 4 }));
            Assert.That(score.Chapters(StoryScene.Closing, timing), Is.EqualTo(new[] { 5, 7.3, 10.2 }).Within(1e-9));
            foreach (var scene in new[] { StoryScene.Opening, StoryScene.Montage, StoryScene.Closing })
            {
                var chapters = score.Chapters(scene, timing);
                Assert.That(chapters, Is.Ordered, scene.ToString());
                Assert.That(chapters.Length, Is.GreaterThanOrEqualTo(2), "One press must never skip a whole scene: " + scene);
            }
        }

        [Test]
        public void TheHintRisesInHoldsThenFadesOnBlack()
        {
            var score = new PortraitScore();
            Assert.That(score.SkipHintText, Is.EqualTo("点击右上角「跳过」可跳过动画"));
            Assert.That(score.SkipHintDuration, Is.EqualTo(2.5).Within(1e-9));
            var commands = new List<CutsceneCommand>();
            CutsceneCommand Line(double time)
            {
                CutsceneTimeline.DrawSkipHint(time, score, commands);
                Assert.That(commands.Count, Is.EqualTo(2));
                Assert.That(commands[0].Ink, Is.EqualTo(CutsceneInk.Ink));
                Assert.That(commands[0].Width, Is.EqualTo(PortraitScore.Width));
                Assert.That(commands[0].Height, Is.EqualTo(PortraitScore.Height));
                return commands[1];
            }
            Assert.That(Line(0).Alpha, Is.Zero);
            Assert.That(Line(0).Y, Is.EqualTo(PortraitScore.Height / 2d + 12), "The line rises 12 px as it appears.");
            Assert.That(Line(.25).Alpha, Is.EqualTo(.875).Within(1e-9), "ease-out entry");
            Assert.That(Line(.5).Alpha, Is.EqualTo(1));
            Assert.That(Line(.5).Y, Is.EqualTo(PortraitScore.Height / 2d));
            Assert.That(Line(2.1).Alpha, Is.EqualTo(1));
            Assert.That(Line(2.3).Alpha, Is.EqualTo(.75).Within(1e-9), "ease-in exit");
            Assert.That(Line(2.5).Alpha, Is.Zero);
            Assert.That(Line(1).Text, Is.EqualTo(score.SkipHintText));
            Assert.That(Line(1).Ink, Is.EqualTo(CutsceneInk.Bone));
            Assert.That(Line(1).Align, Is.EqualTo(CutsceneTextAlign.Center));
            Assert.That(Line(1).X, Is.EqualTo(PortraitScore.Width / 2d));
        }
    }
}
