namespace AudiobookshelfM4bCreator;

/// <summary>Compares strings so that embedded numbers sort by value ("2" before "10").</summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x == y) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int ix = 0, iy = 0;

        while (ix < x.Length && iy < y.Length)
        {
            bool xDigit = char.IsDigit(x[ix]);
            bool yDigit = char.IsDigit(y[iy]);

            if (xDigit && yDigit)
            {
                // Compare numeric segments by value
                int xStart = ix, yStart = iy;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;

                var xNum = x.AsSpan(xStart, ix - xStart);
                var yNum = y.AsSpan(yStart, iy - yStart);

                // Skip leading zeros for comparison
                var xTrimmed = xNum.TrimStart('0');
                var yTrimmed = yNum.TrimStart('0');

                if (xTrimmed.Length != yTrimmed.Length)
                    return xTrimmed.Length.CompareTo(yTrimmed.Length);

                int numCmp = xTrimmed.CompareTo(yTrimmed, StringComparison.Ordinal);
                if (numCmp != 0) return numCmp;
            }
            else
            {
                int cmp = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                if (cmp != 0) return cmp;
                ix++;
                iy++;
            }
        }

        return x.Length.CompareTo(y.Length);
    }
}
