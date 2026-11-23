using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Bamen.Core.Dances;

/// <summary>What reading a dance came to: the dance, or why there is none.</summary>
public sealed record DanceLoad(Dance? Dance, string? Problem)
{
    public static DanceLoad Failed(string problem) => new(null, problem);
}

/// <summary>Reads the file a `dance` line names (the path as written, and the folder of the .bamen file).</summary>
public delegate DanceLoad DanceSource(string written, string? folder);

/// <summary>
/// Reads a Shosa file by running `shosa clip` (DESIGN.md 1.3, 3.3), as Shosa reads a song by
/// running Lily#'s `lysc`. `shosa` is BAMEN_SHOSA, then the PATH, then the newest Shosa extension
/// installed in VS Code (`server/shosa`).
/// </summary>
public static class ShosaClipReader
{
    public static DanceLoad Read(string written, string? folder)
    {
        string path = Path.GetFullPath(Path.Combine(folder ?? Directory.GetCurrentDirectory(), written));
        if (!File.Exists(path))
            return DanceLoad.Failed($"There is no file {path}.");
        var (program, problem) = Find();
        if (program is null)
            return DanceLoad.Failed(problem!);

        string json = Path.Combine(Path.GetTempPath(), $"bamen-{Guid.NewGuid():N}.json");
        try
        {
            var (exit, _, error) = Run(program, ["clip", path, "-o", json]);
            if (!File.Exists(json))
                return DanceLoad.Failed($"shosa could not read {written}: {FirstLine(error) ?? $"exit code {exit}"}");
            var dance = Parse(File.ReadAllText(json), out string? note);
            if (dance is null)
                return DanceLoad.Failed($"shosa's clip of {written} could not be read: {note}");
            // The dance has mistakes of its own: Bamen shoots it as Shosa solved it, and says so.
            if (exit != 0 && error.Split('\n').FirstOrDefault(l => l.Contains(": error ")) is { } first)
                return new DanceLoad(dance, $"{written} has errors of its own (run `shosa check` on it): {first.Trim()}");
            return new DanceLoad(dance, note);
        }
        finally
        {
            try { File.Delete(json); } catch (IOException) { }
        }
    }

    private static string? FirstLine(string text) =>
        text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);

    /// <summary>
    /// The clip JSON of `shosa clip`. A file of several scores gives an array: M0 shoots the first
    /// (the unnamed one, when there is one) and says so in <paramref name="note"/>.
    /// </summary>
    public static Dance? Parse(string json, out string? note)
    {
        note = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                var clips = root.EnumerateArray().ToList();
                if (clips.Count == 0)
                {
                    note = "it has no score.";
                    return null;
                }
                var chosen = clips.FirstOrDefault(c => c.TryGetProperty("name", out var n) && n.GetString() == "");
                if (chosen.ValueKind != JsonValueKind.Object)
                    chosen = clips[0];
                note = $"The dance has {clips.Count} scores; the first ({Label(chosen)}) is shot.";
                return FromClip(chosen);
            }
            return FromClip(root);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            note = ex.Message;
            return null;
        }
    }

    private static string Label(JsonElement clip) =>
        clip.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name ? $"score {name}" : "the unnamed score";

    private static Dance FromClip(JsonElement clip)
    {
        string title = clip.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        double fps = clip.GetProperty("fps").GetDouble();
        double totalBeats = clip.GetProperty("totalBeats").GetDouble();
        var tempos = new List<TempoChange>();
        if (clip.TryGetProperty("tempos", out var ts))
            foreach (var x in ts.EnumerateArray())
                tempos.Add(new TempoChange(x.GetProperty("beat").GetDouble(), x.GetProperty("bpm").GetDouble()));
        else if (clip.TryGetProperty("tempo", out var tempo))
            tempos.Add(new TempoChange(0, tempo.GetDouble()));
        var meters = new List<MeterChange>();
        if (clip.TryGetProperty("meters", out var ms))
            foreach (var x in ms.EnumerateArray())
                meters.Add(new MeterChange(x.GetProperty("beat").GetDouble(), x.GetProperty("beatsPerBar").GetInt32()));
        else if (clip.TryGetProperty("beatsPerBar", out var bpb))
            meters.Add(new MeterChange(0, bpb.GetInt32()));
        var joints = clip.GetProperty("joints").EnumerateArray().Select(j => j.GetString()!).ToList();
        var figures = new List<DanceFigure>();
        foreach (var f in clip.GetProperty("figures").EnumerateArray())
        {
            var frames = f.GetProperty("frames").EnumerateArray()
                .Select(frame => frame.EnumerateArray().Select(v => v.GetSingle()).ToArray())
                .ToList();
            figures.Add(new DanceFigure(f.GetProperty("name").GetString()!, frames));
        }
        string? music = clip.TryGetProperty("music", out var m) && m.TryGetProperty("file", out var file) ? file.GetString() : null;
        return new Dance(title, fps, totalBeats, tempos, meters, joints, figures, music);
    }

    private static (string? Program, string? Problem) Find()
    {
        const string Install = "Shosa, which solves the dance, is needed: install the Shosa extension for VS Code, or name its shosa in BAMEN_SHOSA.";
        string name = OperatingSystem.IsWindows() ? "shosa.exe" : "shosa";
        if (Environment.GetEnvironmentVariable("BAMEN_SHOSA") is { Length: > 0 } named)
            return File.Exists(named) ? (named, null) : (null, $"BAMEN_SHOSA names {named}, which is not there. " + Install);
        string? found = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => { try { return Path.Combine(folder.Trim('"'), name); } catch (ArgumentException) { return ""; } })
            .FirstOrDefault(File.Exists);
        found ??= Installed(name);
        return found is null ? (null, Install) : (found, null);
    }

    /// <summary>The shosa of the newest Shosa extension in VS Code's extensions folder, or null.</summary>
    private static string? Installed(string name)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (string editor in new[] { ".vscode", ".vscode-insiders" })
        {
            string extensions = Path.Combine(home, editor, "extensions");
            if (!Directory.Exists(extensions))
                continue;
            var newest = Directory.GetDirectories(extensions, "yotsuda.shosa-*")
                .Select(dir => (Dir: dir, Version: Version.TryParse(Path.GetFileName(dir)["yotsuda.shosa-".Length..].Split('-')[0], out var v) ? v : null))
                .Where(x => x.Version is not null && File.Exists(Path.Combine(x.Dir, "server", name)))
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();
            if (newest.Dir is not null)
                return Path.Combine(newest.Dir, "server", name);
        }
        return null;
    }

    private static (int Exit, string Output, string Error) Run(string program, List<string> arguments)
    {
        try
        {
            var start = new ProcessStartInfo(program)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,   // a language server's own stdin is the editor's messages
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (string argument in arguments)
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120_000))
            {
                process.Kill(entireProcessTree: true);
                return (-1, "", "shosa did not answer within two minutes.");
            }
            return (process.ExitCode, output.Result, error.Result);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return (-1, "", ex.Message);
        }
    }
}
