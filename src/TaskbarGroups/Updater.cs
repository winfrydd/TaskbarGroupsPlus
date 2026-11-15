using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace TaskbarGroups;

/// <summary>
/// Updates from GitHub Releases. Only the editor window ever checks (never the popup), and the
/// only thing sent is a plain request to api.github.com for the latest release.
///
/// Applying an update:
///   1. download the release zip and unpack it into &lt;app&gt;\.update\
///   2. start .update\TaskbarGroups.exe --apply-update &lt;app&gt; &lt;pid&gt; and close the editor
///   3. that copy waits for the editor to exit, copies itself over the program files
///      (config\ and Shortcuts\ are never touched), and reopens the editor
///   4. the next editor start deletes .update\
/// </summary>
static class Updater
{
    public sealed record Release(Version Version, string Tag, string ZipUrl, string Notes, string PageUrl);

    const string UpdateDirName = ".update";
    static string UpdateDir => Path.Combine(Paths.AppDir, UpdateDirName);

    static HttpClient NewClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TaskbarGroupsPlus-updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public static bool Configured => !Paths.Repo.StartsWith("OWNER/");

    /// <summary>The newest release if it's newer than this build, else null. Throws on network errors.</summary>
    public static async Task<Release?> CheckAsync(CancellationToken ct = default)
    {
        if (!Configured) return null;
        using var http = NewClient();
        http.Timeout = TimeSpan.FromSeconds(10);
        using var resp = await http.GetAsync($"https://api.github.com/repos/{Paths.Repo}/releases/latest", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no releases yet
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var ver)) return null;
        ver = new Version(ver.Major, ver.Minor, Math.Max(ver.Build, 0));
        if (ver <= Paths.Version) return null;

        string? zip = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            string name = a.GetProperty("name").GetString() ?? "";
            if (name.StartsWith("TaskbarGroupsPlus", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                zip = a.GetProperty("browser_download_url").GetString();
                break;
            }
        }
        if (zip == null || !zip.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) return null;

        string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        return new Release(ver, tag, zip, notes, page);
    }

    /// <summary>Downloads and unpacks the release, then starts the copy step. Caller exits afterwards.</summary>
    public static async Task DownloadAndStartAsync(Release r)
    {
        // Fails early, with a clear message, if the program folder isn't writable.
        if (Directory.Exists(UpdateDir)) Directory.Delete(UpdateDir, true);
        Directory.CreateDirectory(UpdateDir);

        string zipFile = Path.Combine(UpdateDir, "release.zip");
        using (var http = NewClient())
        using (var resp = await http.GetAsync(r.ZipUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(zipFile);
            await resp.Content.CopyToAsync(fs);
        }

        string files = Path.Combine(UpdateDir, "files");
        ZipFile.ExtractToDirectory(zipFile, files); // rejects entries that escape the folder
        File.Delete(zipFile);

        // Accept a zip that has everything inside one top-level folder too.
        string newExe = Path.Combine(files, "TaskbarGroups.exe");
        if (!File.Exists(newExe))
        {
            var sub = Directory.GetDirectories(files).FirstOrDefault(d => File.Exists(Path.Combine(d, "TaskbarGroups.exe")));
            if (sub == null) throw new InvalidDataException("The downloaded update doesn't contain TaskbarGroups.exe.");
            newExe = Path.Combine(sub, "TaskbarGroups.exe");
        }

        Process.Start(new ProcessStartInfo(newExe)
        {
            ArgumentList = { "--apply-update", Paths.AppDir, Environment.ProcessId.ToString() },
            UseShellExecute = false,
        });
    }

    /// <summary>Runs inside the downloaded copy: --apply-update &lt;target dir&gt; &lt;pid to wait for&gt;.</summary>
    public static int Apply(string target, string pidText)
    {
        string source = Paths.AppDir;
        try
        {
            if (int.TryParse(pidText, out int pid) && pid > 0)
            {
                try { using var p = Process.GetProcessById(pid); p.WaitForExit(30_000); }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                { /* already gone, or not ours to wait for */ }
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(source, file);
                string top = rel.Split(Path.DirectorySeparatorChar)[0];
                if (top.Equals("config", StringComparison.OrdinalIgnoreCase) ||
                    top.Equals("Shortcuts", StringComparison.OrdinalIgnoreCase) ||
                    rel.Equals("error.log", StringComparison.OrdinalIgnoreCase)) continue;

                string dest = Path.Combine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                CopyWithRetry(file, dest);
            }

            Process.Start(new ProcessStartInfo(Path.Combine(target, "TaskbarGroups.exe")) { UseShellExecute = false, WorkingDirectory = target });
            return 0;
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(target, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Update: {ex}\r\n\r\n"); } catch { }
            MessageBox.Show(
                "The update couldn't be installed:\n\n" + ex.Message +
                "\n\nYour groups are untouched. You can download the new version from GitHub and copy it over the old one.",
                Paths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    /// <summary>A popup that's open at that moment keeps the dll locked for a moment; wait for it.</summary>
    static void CopyWithRetry(string from, string to)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { File.Copy(from, to, true); return; }
            catch (IOException) when (attempt < 40) { Thread.Sleep(250); }
            catch (UnauthorizedAccessException) when (attempt < 40) { Thread.Sleep(250); }
        }
    }

    /// <summary>Removes what a finished update left behind. Safe to call on every editor start.</summary>
    public static void CleanUp()
    {
        try
        {
            if (Directory.Exists(UpdateDir) && !Paths.AppDir.Contains(UpdateDirName, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(UpdateDir, true);
        }
        catch { /* the copy step may still be closing; try again next time */ }
    }
}
