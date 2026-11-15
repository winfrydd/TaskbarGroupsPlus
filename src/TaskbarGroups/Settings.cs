namespace TaskbarGroups;

/// <summary>App-wide preferences in config\settings.ini (plain key=value lines).</summary>
static class Settings
{
    static string FilePath => Path.Combine(Paths.ConfigDir, "settings.ini");

    /// <summary>Look for a new version each time the main window opens. Off = only when you click "Check for updates".</summary>
    public static bool AutoCheckUpdates { get; set; }

    /// <summary>False until the user has answered the one-time "check for updates?" question.</summary>
    public static bool AskedAboutUpdates { get; set; }

    /// <summary>A version the user chose to skip: no button for it (a newer one still shows).</summary>
    public static string SkippedVersion { get; set; } = "";

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim(), value = line[(eq + 1)..].Trim();
                switch (key)
                {
                    case "AutoCheckUpdates":
                        AutoCheckUpdates = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        AskedAboutUpdates = true;
                        break;
                    case "SkippedVersion": SkippedVersion = value; break;
                }
            }
        }
        catch (Exception ex) { Paths.LogError(ex, "Loading settings"); }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.ConfigDir);
            File.WriteAllLines(FilePath, [
                $"AutoCheckUpdates={(AutoCheckUpdates ? "true" : "false")}",
                $"SkippedVersion={SkippedVersion}",
            ]);
        }
        catch (Exception ex) { Paths.LogError(ex, "Saving settings"); }
    }
}
