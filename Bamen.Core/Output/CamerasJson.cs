using System.Text;
using System.Text.Json;
using Bamen.Core.Cameras;

namespace Bamen.Core.Output;

/// <summary>
/// What `bamen cameras` writes: every shot with its solved camera, as JSON for a program (the preview,
/// a glTF writer, a test) or an AI assistant to read. Metres, seconds and degrees; y up, the audience toward +z.
/// </summary>
public static class CamerasJson
{
    public static string Write(BamenCompilation compilation)
    {
        var bound = compilation.Bound;
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            if (bound.Title is { } title)
                w.WriteString("title", title);
            if (bound.DanceFile is { } danceFile)
                w.WriteString("dance", danceFile);
            if (bound.Dance is { } dance)
            {
                w.WriteNumber("fps", dance.Fps);
                w.WriteNumber("seconds", R(dance.TotalSeconds));
            }
            w.WriteNumber("aspect", R(SolvedCamera.Aspect));
            w.WriteStartArray("shots");
            foreach (var c in compilation.Cameras)
            {
                var shot = c.Shot;
                w.WriteStartObject();
                w.WriteNumber("shot", shot.Number);
                w.WriteNumber("start", R(shot.Start));
                w.WriteNumber("end", R(shot.End));
                w.WriteNumber("firstFrame", c.FirstFrame);
                w.WriteNumber("lastFrame", c.LastFrame);
                w.WriteString("size", Words.Of(shot.Size));
                w.WriteString("angle", Words.Of(shot.Angle));
                w.WriteString("from", Words.Of(shot.Side));
                w.WriteStartArray("look");
                foreach (var t in shot.Look)
                    w.WriteStringValue(t.ToString());
                w.WriteEndArray();
                Vector(w, "facing", c.Facing);
                w.WriteStartObject("camera");
                Vector(w, "position", c.Position);
                Vector(w, "aim", c.Aim);
                Vector(w, "forward", c.Forward);
                Vector(w, "up", c.Up);
                w.WriteNumber("focalLength", c.FocalLength);
                w.WriteNumber("verticalFov", R(c.VerticalFov));
                w.WriteNumber("horizontalFov", R(c.HorizontalFov));
                w.WriteNumber("pitch", R(c.Pitch));
                w.WriteNumber("distance", R((c.Aim - c.Position).Length));
                w.WriteEndObject();
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
