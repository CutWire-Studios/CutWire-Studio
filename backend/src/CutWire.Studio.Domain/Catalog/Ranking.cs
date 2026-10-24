namespace CutWire.Studio.Domain.Catalog;

public static class Ranking
{
    /// <summary>z for a 95% two-sided confidence interval.</summary>
    public const double Z95 = 1.959963984540054;

    public static readonly TimeSpan HotHalfLife = TimeSpan.FromDays(7);

    /// <summary>Lower bound of the Wilson score interval for the share of positive votes (0 with no votes).</summary>
    public static double Wilson(double up, double down)
    {
        var n = up + down;
        if (n <= 0) return 0;
        var p = up / n;
        var z2 = Z95 * Z95;
        var centre = p + z2 / (2 * n);
        var margin = Z95 * Math.Sqrt((p * (1 - p) + z2 / (4 * n)) / n);
        return Math.Max(0, (centre - margin) / (1 + z2 / n));
    }

    /// <summary>The Wilson score halved every <see cref="HotHalfLife"/> since publication.</summary>
    public static double Hot(double wilson, DateTimeOffset publishedAt, DateTimeOffset now)
    {
        var age = now - publishedAt;
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        return wilson * Math.Pow(0.5, age / HotHalfLife);
    }
}
