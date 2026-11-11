using System;

namespace Offtrack.Core
{
    public static class InputRules
    {
        public static int PointerDirection(double positionX, double screenWidth)
        {
            if (double.IsNaN(screenWidth) || double.IsInfinity(screenWidth) || screenWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(screenWidth));
            if (double.IsNaN(positionX) || double.IsInfinity(positionX))
                throw new ArgumentOutOfRangeException(nameof(positionX));
            return positionX < screenWidth / 2 ? -1 : 1;
        }
    }
}
