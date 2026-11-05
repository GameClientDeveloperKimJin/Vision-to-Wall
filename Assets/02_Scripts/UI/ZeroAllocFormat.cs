using System.Text;

namespace VisionToWall.UI
{
    /// <summary>
    /// StringBuilder number appends without the boxing / ToString allocation of <c>StringBuilder.Append(int)</c>.
    /// Main thread only (shares one scratch buffer).
    /// </summary>
    public static class ZeroAllocFormat
    {
        private static readonly char[] Scratch = new char[20];
        private static readonly long[] Pow10 = { 1, 10, 100, 1000, 10000, 100000, 1000000 };

        public const int MaxDecimals = 6;

        public static StringBuilder AppendInt(this StringBuilder sb, long value)
        {
            if (value == 0) return sb.Append('0');

            ulong u;
            if (value < 0)
            {
                sb.Append('-');
                u = (ulong)(-(value + 1)) + 1; // safe for long.MinValue
            }
            else
            {
                u = (ulong)value;
            }

            int i = Scratch.Length;
            while (u > 0)
            {
                Scratch[--i] = (char)('0' + (int)(u % 10));
                u /= 10;
            }
            return sb.Append(Scratch, i, Scratch.Length - i);
        }

        /// <summary>Fixed-point with <paramref name="decimals"/> digits (0..6). NaN/Infinity append "0".</summary>
        public static StringBuilder AppendFixed(this StringBuilder sb, float value, int decimals)
        {
            if (decimals < 0) decimals = 0;
            if (decimals > MaxDecimals) decimals = MaxDecimals;
            if (float.IsNaN(value) || float.IsInfinity(value)) return sb.Append('0');

            long pow = Pow10[decimals];
            double abs = value < 0f ? -(double)value : value;
            long scaled = (long)System.Math.Round(abs * pow, System.MidpointRounding.AwayFromZero);
            if (value < 0f && scaled != 0) sb.Append('-');

            AppendInt(sb, scaled / pow);
            if (decimals == 0) return sb;

            sb.Append('.');
            long frac = scaled % pow;
            for (long p = pow / 10; p > 0; p /= 10)
            {
                sb.Append((char)('0' + (int)(frac / p % 10)));
            }
            return sb;
        }
    }
}
