using NUnit.Framework;
using Offtrack.Runtime;
using Offtrack.Runtime.Presentation;
using UnityEditor;
using UnityEngine;

namespace Offtrack.Tests.EditMode
{
    public sealed class V2MainScreenConfigurationTests
    {
        [Test]
        public void GeneratedAssetCarriesTheReviewedMainScreenLayout()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Offtrack/Config/GameConfig.asset");
            Assert.That(config.View.MainScreenVisualVersion, Is.EqualTo(4));
            Assert.That(config.ShellView.MainScreenRevision, Is.EqualTo(5));
            // V2-2: doubled lift (reference pixels) and the meter HUD layout.
            Assert.That(config.View.LiftY, Is.GreaterThanOrEqualTo(42));
            Assert.That(config.ShellView.TimerFontSize, Is.GreaterThanOrEqualTo(40));
            // Plinth contents are plinth-local: compare against the plinth's own half height.
            Assert.That(config.ShellView.ScaleOrigin.y - config.ShellView.ScaleMajorTickHeight / 2,
                Is.GreaterThan(-config.ShellView.StageSize.y / 2),
                "The ruler stays inside the shortened plinth.");
            Assert.That(config.Machine.StartDuration, Is.EqualTo(.6).Within(1e-9));
            Assert.That(config.Machine.StopDuration, Is.EqualTo(.8).Within(1e-9));
            Assert.That(config.View.ContactFontSize, Is.GreaterThanOrEqualTo(24));
            Assert.That(config.View.ContactNumberSize.y, Is.GreaterThan(config.View.ContactFontSize));
            Assert.That(config.ShellView.ScaleTickSize.x, Is.GreaterThanOrEqualTo(2));
            Assert.That(config.ShellView.CompleteTitlePosition.y,
                Is.LessThan(config.ShellView.StagePosition.y - config.ShellView.StageSize.y / 2));
        }

        [Test]
        public void UpgradeFromBatchOneIsOneTimeAndKeepsLaterAuthorTuning()
        {
            var view = new ViewSettings { MainScreenVisualVersion = 1, ContactFontSize = 16, LaneClearance = 12 };
            var shell = new ShellViewSettings { MainScreenRevision = 1, ScaleTickSize = new Vector2(1, 6),
                CompleteTitlePosition = new Vector2(0, -416), CompleteBodyPosition = new Vector2(0, -472) };
            view.UpgradeMainScreenVisuals();
            shell.UpgradeMainScreen();
            Assert.That(view.ContactFontSize, Is.EqualTo(24));
            Assert.That(view.LaneClearance, Is.EqualTo(16));
            Assert.That(shell.ScaleTickSize.x, Is.EqualTo(2));
            Assert.That(shell.CompleteTitlePosition.y, Is.EqualTo(-722));
            Assert.That(shell.CompleteBodyPosition.y, Is.EqualTo(-778));
            view.ContactFontSize = 26;
            shell.CompleteBodyPosition = new Vector2(0, -770);
            view.UpgradeMainScreenVisuals();
            shell.UpgradeMainScreen();
            Assert.That(view.ContactFontSize, Is.EqualTo(26));
            Assert.That(shell.CompleteBodyPosition.y, Is.EqualTo(-770));
        }
    }
}
