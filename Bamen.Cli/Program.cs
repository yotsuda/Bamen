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
      bamen preview FILE [--stdin] [-o F]
                                  what the VS Code preview draws, as one JSON: the dance's clip,
                                  every shot's time, line and camera, and the diagnostics.
                                  --stdin: the text from the input (unsaved), FILE for its folder

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
bool fromInput = false;
for (int i = 2; i < args.Length; i++)
{
    if (args[i] is "-o" or "--output" && i + 1 < args.Length)
    {
        output = args[++i];
    }
    else if (args[i] == "--stdin" && command == "preview")
    {
        fromInput = true;
    }
    else
    {
        Console.Error.WriteLine($"bamen: unknown option '{args[i]}'");
        return 2;
    }
}
if (command is not ("check" or "cameras" or "sheet" or "preview"))
{
    Console.Error.WriteLine($"bamen: unknown command '{command}'");
    Console.WriteLine(Usage);
    return 2;
}
if (!fromInput && !File.Exists(path))
{
    Console.Error.WriteLine($"bamen: no such file: {path}");
    return 2;
}

string text;
if (fromInput)
{
    using var input = new StreamReader(Console.OpenStandardInput(), new System.Text.UTF8Encoding(false));
    text = input.ReadToEnd();
}
else
{
    text = File.ReadAllText(path);
}
var compilation = BamenCompilation.Compile(text, ShosaClipReader.Read, Path.GetDirectoryName(Path.GetFullPath(path)));

// The preview's JSON carries the diagnostics itself.
if (command != "preview")
{
    foreach (string problem in CheckReport.Problems(compilation, path))
        Console.Error.WriteLine(problem);
}

switch (command)
{
    case "preview":
    {
        string preview = PreviewJson.Write(compilation);
        if (output is null)
        {
            using var stdout = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false));
            stdout.Write(preview);
        }
        else
        {
            File.WriteAllText(output, preview);
        }
        // The JSON is the answer, problems and all: the extension reads it whatever the file's state.
        return 0;
    }

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
