using System.Diagnostics;

namespace TaskbarGroups;

/// <summary>Startup profiling, only active when the TBG_TIMING environment variable names a log file.</summary>
static class Timing
{
    static readonly string? File_ = Environment.GetEnvironmentVariable("TBG_TIMING") is { Length: > 0 } f ? f : null;
    static readonly List<string> Marks = [];

    public static bool On => File_ != null;

    public static void Mark(string what)
    {
        if (File_ == null) return;
        var ms = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        Marks.Add($"{what}={ms:F0}");
    }

    public static void Flush()
    {
        if (File_ == null) return;
        try { File.AppendAllText(File_, string.Join("  ", Marks) + "\r\n"); } catch { }
    }
}
