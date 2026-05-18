namespace NetworkWatch.Models;

public static class TrafficFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string FormatBytes(ulong bytes)
    {
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {Units[unit]}"
            : $"{value:0.##} {Units[unit]}";
    }

    public static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
            return "0 B/s";

        double value = bytesPerSecond;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {Units[unit]}/s";
    }
}
