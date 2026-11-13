using System.Linq;
using NUnit.Framework;
using Offtrack.Runtime.Presentation;
using UnityEngine;

namespace Offtrack.Tests.PlayMode
{
    public sealed class V2PaletteTests
    {
        [Test]
        public void ScreenshotWhitelistComesDirectlyFromProductionPalette()
        {
            var actual = PaletteAssert.AssertExactPalette(Palette.AllowedColors.ToArray(), "production whitelist");
            CollectionAssert.AreEquivalent(Palette.AllowedColors.Select(PaletteAssert.Rgb), actual);
            Assert.Throws<AssertionException>(() => PaletteAssert.AssertExactPalette(
                new[] { new Color32(1, 2, 3, 255) }, "unapproved fill"));
        }

        [Test]
        public void EveryOpaqueShadeHasExactlyOneApprovedDimmedComposite()
        {
            Assert.That(Palette.OpaqueColors.All(color => color.a == byte.MaxValue), Is.True);
            Assert.That(Palette.DialogDim.a / 255.0, Is.EqualTo(.86).Within(.002));
            foreach (var color in Palette.OpaqueColors)
            {
                Assert.That(Palette.AllowedColors.Contains(color), Is.True);
                Assert.That(Palette.AllowedColors.Contains(Palette.Dimmed(color)), Is.True);
            }
            Assert.That(Palette.AllowedColors.Count,
                Is.EqualTo(Palette.OpaqueColors.Concat(Palette.OpaqueColors.Select(Palette.Dimmed)).Distinct().Count()));
        }
    }
}
