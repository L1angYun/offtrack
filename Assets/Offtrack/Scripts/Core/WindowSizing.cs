using System;

namespace Offtrack.Core
{
    public struct WindowSize
    {
        public readonly int Width;
        public readonly int Height;

        public WindowSize(int width, int height) { Width = width; Height = height; }
    }

    public static class WindowSizing
    {
        /// <summary>
        /// Fits the complete frame when Unity centers it on the whole display after
        /// SetResolution. Work-area coordinates are relative to that display.
        /// Reserve the larger opposing inset on BOTH sides of each axis before fitting
        /// the client; this also handles taskbars at the top or left of the display.
        /// Returns false if no positive client can fit at the display's center.
        /// </summary>
        public static bool TryFitCentered(int width, int height, int displayWidth, int displayHeight,
            int workAreaX, int workAreaY, int workAreaWidth, int workAreaHeight,
            int frameWidth, int frameHeight, out WindowSize result)
        {
            ValidateClientAndFrame(width, height, frameWidth, frameHeight);
            if (displayWidth <= 0) throw new ArgumentOutOfRangeException(nameof(displayWidth));
            if (displayHeight <= 0) throw new ArgumentOutOfRangeException(nameof(displayHeight));
            if (workAreaX < 0) throw new ArgumentOutOfRangeException(nameof(workAreaX));
            if (workAreaY < 0) throw new ArgumentOutOfRangeException(nameof(workAreaY));
            if (workAreaWidth <= 0 || (long)workAreaX + workAreaWidth > displayWidth)
                throw new ArgumentOutOfRangeException(nameof(workAreaWidth));
            if (workAreaHeight <= 0 || (long)workAreaY + workAreaHeight > displayHeight)
                throw new ArgumentOutOfRangeException(nameof(workAreaHeight));

            int horizontalInset = Math.Max(workAreaX, displayWidth - workAreaX - workAreaWidth);
            int verticalInset = Math.Max(workAreaY, displayHeight - workAreaY - workAreaHeight);
            long safeWidth = displayWidth - 2L * horizontalInset;
            long safeHeight = displayHeight - 2L * verticalInset;
            if (safeWidth <= frameWidth || safeHeight <= frameHeight)
            {
                result = default;
                return false;
            }
            result = Fit(width, height, (int)safeWidth, (int)safeHeight, frameWidth, frameHeight);
            return true;
        }

        /// <summary>
        /// Fits client pixels inside a display work area after reserving window chrome.
        /// Never enlarges the request; integer dimensions round down by less than one pixel.
        /// A work area smaller than its chrome reserve is saturated to a one-pixel client.
        /// </summary>
        public static WindowSize Fit(int width, int height, int workAreaWidth, int workAreaHeight,
            int frameWidth, int frameHeight)
        {
            ValidateClientAndFrame(width, height, frameWidth, frameHeight);
            if (workAreaWidth <= 0) throw new ArgumentOutOfRangeException("workAreaWidth");
            if (workAreaHeight <= 0) throw new ArgumentOutOfRangeException("workAreaHeight");

            int availableWidth = Math.Min(width, Math.Max(1, workAreaWidth - frameWidth));
            int availableHeight = Math.Min(height, Math.Max(1, workAreaHeight - frameHeight));
            // Integer cross-products select the limiting axis without floating-point rounding
            // making an exactly fitting dimension one pixel smaller.
            if ((long)availableWidth * height <= (long)availableHeight * width)
                return new WindowSize(availableWidth, Math.Max(1, (int)((long)height * availableWidth / width)));
            return new WindowSize(Math.Max(1, (int)((long)width * availableHeight / height)), availableHeight);
        }

        private static void ValidateClientAndFrame(int width, int height, int frameWidth, int frameHeight)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (frameWidth < 0) throw new ArgumentOutOfRangeException(nameof(frameWidth));
            if (frameHeight < 0) throw new ArgumentOutOfRangeException(nameof(frameHeight));
        }
    }
}
