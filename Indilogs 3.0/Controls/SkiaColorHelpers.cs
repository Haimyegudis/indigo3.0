using System;
using SkiaSharp;

namespace IndiLogs_3._0.Controls
{
    internal static class SkiaColorHelpers
    {
        internal static SKColor LightenColor(SKColor c, float amount)
        {
            int r = Math.Min(255, (int)(c.Red + (255 - c.Red) * amount));
            int g = Math.Min(255, (int)(c.Green + (255 - c.Green) * amount));
            int b = Math.Min(255, (int)(c.Blue + (255 - c.Blue) * amount));
            return new SKColor((byte)r, (byte)g, (byte)b, c.Alpha);
        }

        internal static SKColor DarkenColor(SKColor c, float amount)
        {
            int r = Math.Max(0, (int)(c.Red * (1 - amount)));
            int g = Math.Max(0, (int)(c.Green * (1 - amount)));
            int b = Math.Max(0, (int)(c.Blue * (1 - amount)));
            return new SKColor((byte)r, (byte)g, (byte)b, c.Alpha);
        }
    }
}
