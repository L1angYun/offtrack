using System;
using NUnit.Framework;
using Offtrack.Core;

namespace Offtrack.Tests.EditMode
{
    public sealed class WindowSizingTests
    {
        // Chrome dimensions are independent Win32 AdjustWindowRectExForDpi observations
        // for a resizable, captioned, menu-less window; the last case is a conservative
        // 2x bound on the 96-DPI frame, larger than the observed 192-DPI frame.
        [TestCase(585, 1266, 1920, 1080, 0, 0, 1920, 1008, 22, 56, TestName = "CenteredOuterFrameFits_Win11_1080p_150Percent")]
        [TestCase(585, 1266, 1920, 1080, 0, 0, 1920, 1020, 18, 47, TestName = "CenteredOuterFrameFits_1080p_125Percent")]
        [TestCase(585, 1266, 1920, 1080, 0, 0, 1920, 1032, 16, 39, TestName = "CenteredOuterFrameFits_1080p_100Percent")]
        [TestCase(585, 1266, 1366, 768, 0, 0, 1366, 720, 16, 39, TestName = "CenteredOuterFrameFits_1366x768")]
        [TestCase(1280, 720, 1366, 768, 0, 0, 1366, 696, 22, 56, TestName = "CenteredOuterFrameFits_1366x768_Cutscene")]
        [TestCase(585, 1266, 1920, 1080, 0, 72, 1920, 1008, 22, 56, TestName = "CenteredOuterFrameFits_TopTaskbar")]
        [TestCase(1280, 720, 1366, 768, 120, 0, 1246, 768, 22, 56, TestName = "CenteredOuterFrameFits_LeftTaskbar")]
        [TestCase(585, 1266, 500, 1400, 72, 0, 428, 1400, 22, 56, TestName = "CenteredOuterFrameFits_NarrowPortraitLeftTaskbar")]
        [TestCase(585, 1266, 3840, 2160, 0, 0, 3840, 2088, 22, 56, TestName = "CenteredOuterFrameFits_4K_MainKeepsRequestedSize")]
        [TestCase(1280, 720, 3840, 2160, 0, 0, 3840, 2088, 22, 56, TestName = "CenteredOuterFrameFits_4K_CutsceneKeepsRequestedSize")]
        [TestCase(585, 1266, 1920, 1080, 0, 0, 1920, 984, 26, 71, TestName = "CenteredOuterFrameFits_1080p_200Percent")]
        [TestCase(585, 1266, 1920, 1080, 0, 96, 1920, 984, 32, 78, TestName = "CenteredOuterFrameFits_Conservative200PercentChrome")]
        public void CenteredOuterFrameFitsWorkAreaAndPreservesAspect(int width, int height,
            int displayWidth, int displayHeight, int workX, int workY, int workWidth, int workHeight,
            int actualFrameWidth, int actualFrameHeight)
        {
            Assert.That(WindowSizing.TryFitCentered(width, height, displayWidth, displayHeight,
                workX, workY, workWidth, workHeight, 32, 96, out var result), Is.True);
            int outerWidth = result.Width + actualFrameWidth;
            int outerHeight = result.Height + actualFrameHeight;
            int outerX = (displayWidth - outerWidth) / 2;
            int outerY = (displayHeight - outerHeight) / 2;
            Assert.That(outerX, Is.GreaterThanOrEqualTo(workX), "Outer left crosses the taskbar.");
            Assert.That(outerY, Is.GreaterThanOrEqualTo(workY), "Outer top crosses the taskbar.");
            Assert.That(outerX + outerWidth, Is.LessThanOrEqualTo(workX + workWidth), "Outer right crosses the taskbar.");
            Assert.That(outerY + outerHeight, Is.LessThanOrEqualTo(workY + workHeight), "Outer bottom crosses the taskbar.");
            Assert.That(result.Width, Is.InRange(1, width));
            Assert.That(result.Height, Is.InRange(1, height));
            Assert.That(Math.Abs((long)result.Width * height - (long)result.Height * width),
                Is.LessThan(Math.Max(width, height)), "Aspect may differ only by less than one integer pixel.");
            if (displayWidth == 3840)
            {
                Assert.That(result.Width, Is.EqualTo(width));
                Assert.That(result.Height, Is.EqualTo(height));
            }
        }

        [Test]
        public void FullHd150PercentReservesBothSidesOfTheDisplayCenter()
        {
            Assert.That(WindowSizing.TryFitCentered(585, 1266, 1920, 1080,
                0, 0, 1920, 1008, 32, 96, out var result), Is.True);
            Assert.That(result.Width, Is.EqualTo(388));
            Assert.That(result.Height, Is.EqualTo(840));
        }

        [TestCase(1920, 1080, 0, 0, 1920, 500)]
        [TestCase(30, 90, 0, 0, 30, 90)]
        public void ImpossibleCenteredFrameDeclinesResize(int displayWidth, int displayHeight,
            int workX, int workY, int workWidth, int workHeight)
        {
            Assert.That(WindowSizing.TryFitCentered(585, 1266, displayWidth, displayHeight,
                workX, workY, workWidth, workHeight, 32, 96, out var result), Is.False);
            Assert.That(result.Width, Is.Zero);
            Assert.That(result.Height, Is.Zero);
        }

        [TestCase(0, 1080, 0, 0, 1920, 1008)]
        [TestCase(1920, 0, 0, 0, 1920, 1008)]
        [TestCase(1920, 1080, -1, 0, 1920, 1008)]
        [TestCase(1920, 1080, 0, -1, 1920, 1008)]
        [TestCase(1920, 1080, 10, 0, 1920, 1008)]
        [TestCase(1920, 1080, 0, 100, 1920, 1008)]
        public void InvalidDisplayRelativeWorkAreaIsRejected(int displayWidth, int displayHeight,
            int workX, int workY, int workWidth, int workHeight)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WindowSizing.TryFitCentered(585, 1266,
                displayWidth, displayHeight, workX, workY, workWidth, workHeight, 32, 96, out _));
        }

        [TestCase(585, 1266, 3840, 2160)]
        [TestCase(1280, 720, 3840, 2160)]
        [TestCase(585, 1266, 617, 1362)]
        public void RequestedSizeIsPreservedWhenItAlreadyFits(int width, int height, int workWidth, int workHeight)
        {
            var result = WindowSizing.Fit(width, height, workWidth, workHeight, 32, 96);
            Assert.That(result.Width, Is.EqualTo(width));
            Assert.That(result.Height, Is.EqualTo(height));
        }

        [TestCase(585, 1266, 1920, 1032)]
        [TestCase(585, 1266, 1366, 720)]
        [TestCase(1280, 720, 1366, 720)]
        [TestCase(1280, 720, 1024, 700)]
        [TestCase(585, 1266, 500, 1400)]
        [TestCase(1280, 720, 500, 1400)]
        public void OversizedWindowFitsBothAxesAndKeepsAspectWithinOnePixel(int width, int height,
            int workWidth, int workHeight)
        {
            var result = WindowSizing.Fit(width, height, workWidth, workHeight, 32, 96);
            Assert.That(result.Width, Is.InRange(1, Math.Min(width, workWidth - 32)));
            Assert.That(result.Height, Is.InRange(1, Math.Min(height, workHeight - 96)));
            Assert.That(result.Width < width || result.Height < height, Is.True);
            // Integer client dimensions can differ from the exact uniform scale by less than one pixel.
            var scale = Math.Min((workWidth - 32) / (double)width, (workHeight - 96) / (double)height);
            Assert.That(result.Width, Is.EqualTo(width * scale).Within(1));
            Assert.That(result.Height, Is.EqualTo(height * scale).Within(1));
        }

        [Test]
        public void FullHdMainWindowLeavesSpaceForTaskbarAndWindowChrome()
        {
            var result = WindowSizing.Fit(585, 1266, 1920, 1032, 32, 96);
            Assert.That(result.Height, Is.EqualTo(936));
            Assert.That(result.Width, Is.EqualTo(432));
        }

        [Test]
        public void VerySmallWorkAreaStillProducesPositiveClientDimensions()
        {
            var result = WindowSizing.Fit(585, 1266, 20, 20, 32, 96);
            Assert.That(result.Width, Is.EqualTo(1));
            Assert.That(result.Height, Is.EqualTo(1));
        }

        [TestCase(0, 720, 1920, 1032, 32, 96)]
        [TestCase(1280, -1, 1920, 1032, 32, 96)]
        [TestCase(1280, 720, 0, 1032, 32, 96)]
        [TestCase(1280, 720, 1920, -1, 32, 96)]
        [TestCase(1280, 720, 1920, 1032, -1, 96)]
        [TestCase(1280, 720, 1920, 1032, 32, -1)]
        public void InvalidDimensionsAreRejected(int width, int height, int workWidth, int workHeight,
            int frameWidth, int frameHeight)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                WindowSizing.Fit(width, height, workWidth, workHeight, frameWidth, frameHeight));
        }

        // A 4K display doubles the 1080p-authored request (orientation kept); 1080p and smaller keep it.
        [TestCase(1280, 720, 2160, 2560, 1440)]
        [TestCase(585, 1266, 2160, 1170, 2532)]
        [TestCase(1280, 720, 1440, 1707, 960)]
        [TestCase(1280, 720, 1080, 1280, 720)]
        [TestCase(585, 1266, 768, 585, 1266)]
        public void ScaleToDisplayKeepsTheRequestsShareOfTheScreen(int width, int height, int displayHeight,
            int expectedWidth, int expectedHeight)
        {
            var scaled = WindowSizing.ScaleToDisplay(width, height, displayHeight, 1080);
            Assert.That(scaled.Width, Is.EqualTo(expectedWidth));
            Assert.That(scaled.Height, Is.EqualTo(expectedHeight));
        }

        [Test]
        public void On4KTheScaledWindowsStillFitTheWorkArea()
        {
            var cutscene = WindowSizing.ScaleToDisplay(1280, 720, 2160, 1080);
            Assert.That(WindowSizing.TryFitCentered(cutscene.Width, cutscene.Height, 3840, 2160, 0, 0, 3840, 2088, 32, 96, out var landscape), Is.True);
            Assert.That(landscape.Width, Is.EqualTo(2560));
            Assert.That(landscape.Height, Is.EqualTo(1440));
            var main = WindowSizing.ScaleToDisplay(585, 1266, 2160, 1080);
            Assert.That(WindowSizing.TryFitCentered(main.Width, main.Height, 3840, 2160, 0, 0, 3840, 2088, 32, 96, out var portrait), Is.True);
            Assert.That(portrait.Height, Is.GreaterThan(1266 * 1.4), "Portrait grows with the display until it fills the work area.");
            Assert.That(portrait.Height + 96, Is.LessThanOrEqualTo(2160 - 2 * 72));
            Assert.That(Math.Abs((long)portrait.Width * 1266 - (long)portrait.Height * 585), Is.LessThan(1266));
        }
    }
}
