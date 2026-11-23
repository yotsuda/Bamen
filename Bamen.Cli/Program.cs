using Bamen.Core;
using Bamen.Core.Dances;
using Bamen.Core.Output;

const string Usage = """
    bamen — camera and cuts in text, for a moving storyboard (prototype, M0)

      bamen check FILE            diagnostics, then the dance and what each shot came to:
                                  when it plays (seconds and beats), size, angle, side, what it
                                  looks at, and where its camera stands
      bamen cameras FILE [-o F]   every shot's solved camera as JSON (to the output, or to F)
      bamen sheet FILE [-o F]     the storyboard sheet: each shot at its first, middle and last
                                  frame through its camera, as PNG (FILE with .png, or F)

    The `dance` line names a Shosa file; bamen reads it with `shosa clip`. shosa is found in
    BAMEN_SHOSA, then the PATH, then the Shosa extension for VS Code.
    """;

if (args.Length < 2 || args[0] is "-h" or "--help")
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 1 : 0;
}

string command = args[0], path = args[1];
string? output = null;
for (int i = 2; i < args.Length; i++)
{
    if (args[i] is "-o" or "--output" && i + 1 < args.Length)
    {
        output = args[++i];
    }
    else
    {
        Console.Error.WriteLine($"bamen: unknown option '{args[i]}'");
        return 2;
    }
}
if (command is not ("check" or "cameras" or "sheet"))
{
    Console.Error.WriteLine($"bamen: unknown command '{command}'");
    Console.WriteLine(Usage);
    return 2;
}
if (!File.Exists(path))
{
    Console.Error.WriteLine($"bamen: no such file: {path}");
    return 2;
}

string text = File.ReadAllText(path);
var compilation = BamenCompilation.Compile(text, ShosaClipReader.Read, Path.GetDirectoryName(Path.GetFullPath(path)));

foreach (string problem in CheckReport.Problems(compilation, path))
    Console.Error.WriteLine(problem);

switch (command)
{
    case "check":
        foreach (string line in CheckReport.Summary(compilation, path))
            Console.WriteLine(line);
        break;

    case "cameras":
        string json = CamerasJson.Write(compilation);
        if (output is null)
        {
            Console.WriteLine(json);
        }
        else
        {
            File.WriteAllText(output, json + Environment.NewLine);
            Console.WriteLine(output);
        }
        break;

    case "sheet":
        string png = output ?? Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + ".png");
        StoryboardSheet.Render(compilation).SavePng(png);
        Console.WriteLine(png);
        break;
}

return compilation.HasErrors ? 1 : 0;
