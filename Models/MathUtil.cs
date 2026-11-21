namespace PidSimulator.Models;

internal static class MathUtil
{
    public static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    public static (double Min, double Max) ExpandRange(double min, double max)
    {
        if (double.IsNaN(min) || double.IsNaN(max))
            return (0, 1);

        if (max - min < 1e-9)
        {
            double pad = Math.Max(1.0, Math.Abs(max) * 0.1);
            return (min - pad, max + pad);
        }

        double margin = (max - min) * 0.1;
        return (min - margin, max + margin);
    }
}
