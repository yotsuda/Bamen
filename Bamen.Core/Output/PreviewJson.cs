using System.Text;
using System.Text.Json;
using Bamen.Core.Cameras;
using Bamen.Core.Syntax;

namespace Bamen.Core.Output;

/// <summary>
/// What `bamen preview` writes: everything the preview of the VS Code extension draws, in one JSON
/// (DESIGN.md 3.3). The clip of the dance as `shosa clip` wrote it (the dancers are drawn from it as in
/// Shosa's preview), every shot with its time, its line in the source and its solved camera, and the
/// diagnostics. Offsets are into the text in UTF-16 code units (a C# string index, what VS Code counts).
/// </summary>
public static class PreviewJson
{
    public static string Write(BamenCompilation compilation)
    {
        var bound = compilation.Bound;
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            if (bound.Title is { } title)
                w.WriteString("title", title);
            if (bound.Dance?.File is { } file)
                w.WriteString("dance", file);
            w.WriteNumber("aspect", R(SolvedCamera.Aspect));
            w.WritePropertyName("clip");
            if (bound.Dance?.ClipJson is { } clip)
                w.WriteRawValue(clip, skipInputValidation: true);
            else
                w.WriteNullValue();

            w.WriteStartArray("shots");
            foreach (var c in compilation.Cameras)
            {
                var shot = c.Shot;
                w.WriteStartObject();
                w.WriteNumber("shot", shot.Number);
                w.WriteNumber("start", R(shot.Start));
                w.WriteNumber("end", R(shot.End));
                w.WriteNumber("src", shot.Syntax.Span.Start);
                w.WriteNumber("len", shot.Syntax.Span.Length);
                // What the stage says while it plays: "2  medium low  alice".
                var words = new List<string> { Words.Of(shot.Size) };
                if (shot.Angle != ShotAngle.Eye)
                    words.Add(Words.Of(shot.Angle));
                if (shot.Side != ShotSide.Front)
                    words.Add("from " + Words.Of(shot.Side));
                words.Add(string.Join(" ", shot.Look.Select(t => t.ToString())));
                w.WriteString("label", string.Join("  ", words));
                Vector(w, "position", c.Position);
                Vector(w, "aim", c.Aim);
                Vector(w, "up", c.Up);
                w.WriteNumber("verticalFov", R(c.VerticalFov));
                w.WriteNumber("focalLength", c.FocalLength);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("diagnostics");
            foreach (var d in compilation.Diagnostics)
            {
                w.WriteStartObject();
                w.WriteNumber("start", d.Span.Start);
                w.WriteNumber("length", d.Span.Length);
                w.WriteString("severity", d.Severity switch { DiagnosticSeverity.Error => "error", DiagnosticSeverity.Warning => "warning", _ => "info" });
                w.WriteString("code", d.Code);
                w.WriteString("message", d.Message);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static double R(double value) => Math.Round(value, 4);

    private static void Vector(Utf8JsonWriter w, string name, Vec3 v)
    {
        w.WriteStartArray(name);
        w.WriteNumberValue(R(v.X));
        w.WriteNumberValue(R(v.Y));
        w.WriteNumberValue(R(v.Z));
        w.WriteEndArray();
    }
}
