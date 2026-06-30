namespace StripKit.Services;

/// <summary>
/// Orders file names the way a human (and a render queue) expects: <c>frame_2.png</c> before
/// <c>frame_10.png</c>, not after it. An ordinal string sort puts "10" before "2" because '1' &lt; '2';
/// this compares digit-runs numerically instead. Used to sequence a folder of individually-rendered
/// frames, because path tracers don't always zero-pad their output.
/// </summary>
public sealed class NaturalFileNameComparer : IComparer<string>
{
    public static readonly NaturalFileNameComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
            {
                // Compare two digit runs numerically: trim leading zeros, then by length, then digits.
                int sx = ix, sy = iy;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;

                var rx = x.AsSpan(sx, ix - sx).TrimStart('0');
                var ry = y.AsSpan(sy, iy - sy).TrimStart('0');

                if (rx.Length != ry.Length) return rx.Length - ry.Length;
                int cmp = rx.SequenceCompareTo(ry);
                if (cmp != 0) return cmp;
            }
            else
            {
                int cmp = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                if (cmp != 0) return cmp;
                ix++; iy++;
            }
        }

        // Whichever string still has characters left sorts after the exhausted one.
        return (x.Length - ix) - (y.Length - iy);
    }
}
