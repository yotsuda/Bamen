using System.Globalization;
using Bamen.Core.Cameras;
using Bamen.Core.Dances;
using Bamen.Core.Syntax;

namespace Bamen.Core.Output;

/// <summary>
/// What `bamen sheet` draws (DESIGN.md 1.6): a row for each shot, what was written for it on the
/// left, then the picture its camera takes at the first frame of the shot, the middle and the last.
/// A fixed camera that frames the whole of a movement shows it here: nothing it looks at leaves
/// the picture in any of the three. The dancers the shot looks at are drawn in colour (left limbs
/// blue, right red, as in Shosa's sheet), the others grey; the floor has a one-metre grid.
/// </summary>
public static class StoryboardSheet
{
    private const uint Ink = 0x30343A, LeftColor = 0x2F6FDF, RightColor = 0xD9482B, Faint = 0x9AA1AB, Other = 0xC2C7CF,
        FloorLine = 0xE6E9EE, Border = 0x30343A, Title = 0x7A4FBF;
    private const int PictureWidth = 384, PictureHeight = 216, InfoWidth = 260, Margin = 16, Gap = 10, Under = 26, Header = 40;
    private const int TextScale = 2, LineHeight = Canvas.GlyphHeight * TextScale + 4;
    private const double HeadRadius = 0.095;

    public static Canvas Render(BamenCompilation compilation)
    {
        var cameras = compilation.Cameras;
        var dance = compilation.Bound.Dance;
        int rowHeight = PictureHeight + Under + Gap;
        int width = Margin * 2 + InfoWidth + 3 * PictureWidth + 2 * Gap;
        int height = Margin * 2 + Header + Math.Max(1, cameras.Count) * rowHeight;
        var canvas = new Canvas(width, height);

        string title = compilation.Bound.Title ?? "";
        canvas.Text(Margin, Margin, title.Length > 0 ? title : "(no title)", 3, Title);
        if (dance is null || cameras.Count == 0)
        {
            canvas.Text(Margin, Margin + Header, "no shot could be solved: run bamen check", TextScale, Faint);
            return canvas;
        }

        for (int row = 0; row < cameras.Count; row++)
        {
            var camera = cameras[row];
            int y = Margin + Header + row * rowHeight;
            DrawInfo(canvas, compilation, camera, Margin, y);
            int[] frames = [camera.FirstFrame, (camera.FirstFrame + camera.LastFrame) / 2, camera.LastFrame];
            for (int i = 0; i < 3; i++)
            {
                int x = Margin + InfoWidth + i * (PictureWidth + Gap);
                DrawPicture(canvas, dance, camera, frames[i], x, y);
                double seconds = frames[i] / dance.Fps;
                string label = $"beat {N(dance.BeatAt(seconds) + 1)}  {N(seconds)} s";
                canvas.Text(x, y + PictureHeight + 6, label, TextScale, Faint);
            }
        }
        return canvas;
    }

    private static string N(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static void DrawInfo(Canvas canvas, BamenCompilation compilation, SolvedCamera camera, int x, int y)
    {
        var shot = camera.Shot;
        string written = shot.Syntax.Time is { } time ? compilation.Tree.Text.Substring(time.Span.Start, time.Span.Length) : "";
        canvas.Text(x, y, $"shot {shot.Number}", 3, Ink);
        int line = y + Canvas.GlyphHeight * 3 + 8;
        var lines = new List<string>
        {
            $"{Words.Of(shot.Size)} {Words.Of(shot.Angle)}",
            $"from {Words.Of(shot.Side)}",
            written,
            $"{N(shot.Start)}-{N(shot.End)} s",
        };
        lines.AddRange(Wrap("look " + string.Join(" ", shot.Look), InfoWidth - 12));
        lines.Add($"{camera.FocalLength} mm, {N((camera.Aim - camera.Position).Length)} m");
        foreach (string text in lines)
        {
            if (line + LineHeight > y + PictureHeight + Under)
                break;
            canvas.Text(x, line, text, TextScale, text.StartsWith("look ") || text.StartsWith("  ") ? Ink : Faint);
            line += LineHeight;
        }
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        int most = Math.Max(4, width / (Canvas.GlyphAdvance * TextScale));
        string current = "";
        foreach (string word in text.Split(' '))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > most)
            {
                yield return current;
                current = "  " + word;
            }
            else
            {
                current = current.Length == 0 ? word : current + " " + word;
            }
        }
        if (current.Length > 0)
            yield return current;
    }

    private static void DrawPicture(Canvas canvas, Dance dance, SolvedCamera camera, int frame, int left, int top)
    {
        canvas.Clip = (left, top, PictureWidth, PictureHeight);
        double perMetre = camera.FocalLength / (SolvedCamera.SensorHeight / 2) * PictureHeight / 2;   // pixels a metre is, one metre away
        (double X, double Y, double Depth) At(Vec3 p)
        {
            var (x, y, depth) = camera.Project(p);
            return (left + PictureWidth / 2.0 * (1 + x), top + PictureHeight / 2.0 * (1 - y), depth);
        }

        // The floor: a one-metre grid about the dancers, each line cut where it passes behind the camera.
        for (int k = -10; k <= 10; k++)
        {
            FloorSegment(canvas, camera, new Vec3(k, 0, -10), new Vec3(k, 0, 10), At);
            FloorSegment(canvas, camera, new Vec3(-10, 0, k), new Vec3(10, 0, k), At);
        }

        // Far things first, so that the near ones are drawn over them: every bone and head of every dancer.
        var looked = camera.Shot.Look.Select(t => t.Figure).ToHashSet();
        var items = new List<(double Depth, Action Draw)>();
        int head = dance.JointIndex("head"), nose = dance.JointIndex("nose");
        foreach (var figure in dance.Figures)
        {
            bool shown = looked.Contains(figure.Name);
            foreach (var bone in dance.Bones)
            {
                var a = At(figure.Joint(frame, bone.A));
                var b = At(figure.Joint(frame, bone.B));
                if (a.Depth < 0.1 || b.Depth < 0.1)
                    continue;
                uint color = !shown ? Other : bone.Side == 'l' ? LeftColor : bone.Side == 'r' ? RightColor : Ink;
                double thickness = Math.Max(1.5, 0.045 * perMetre / ((a.Depth + b.Depth) / 2));
                items.Add(((a.Depth + b.Depth) / 2, () => canvas.Line(a.X, a.Y, b.X, b.Y, thickness, color)));
            }
            if (dance.Bones.Count == 0)
            {
                for (int j = 0; j < dance.Joints.Count; j++)
                {
                    var p = At(figure.Joint(frame, j));
                    if (p.Depth >= 0.1)
                        items.Add((p.Depth, () => canvas.Disc(p.X, p.Y, Math.Max(1.5, 0.04 * perMetre / p.Depth), shown ? Ink : Other)));
                }
            }
            if (head >= 0)
            {
                var h = At(figure.Joint(frame, head));
                if (h.Depth >= 0.1)
                {
                    double radius = HeadRadius * perMetre / h.Depth;
                    var n = nose >= 0 ? At(figure.Joint(frame, nose)) : h;
                    uint ink = shown ? Ink : Other;
                    string name = figure.Name;
                    items.Add((h.Depth - 0.01, () =>
                    {
                        canvas.Disc(h.X, h.Y, radius, 0xFFFFFF);
                        canvas.Ring(h.X, h.Y, radius, Math.Max(1.2, radius * 0.15), ink);
                        if (n.Depth >= 0.1)
                            canvas.Line(n.X, n.Y, h.X + (n.X - h.X) * 1.7, h.Y + (n.Y - h.Y) * 1.7, Math.Max(1.5, radius * 0.18), ink);
                        canvas.Text((int)(h.X - Canvas.TextWidth(name, 1) / 2.0), (int)(h.Y - radius - 12), name, 1, shown ? Faint : Other);
                    }));
                }
            }
        }
        foreach (var (_, draw) in items.OrderByDescending(i => i.Depth))
            draw();

        canvas.Clip = null;
        canvas.Rect(left, top, PictureWidth, 1, Border);
        canvas.Rect(left, top + PictureHeight - 1, PictureWidth, 1, Border);
        canvas.Rect(left, top, 1, PictureHeight, Border);
        canvas.Rect(left + PictureWidth - 1, top, 1, PictureHeight, Border);
    }

    private static void FloorSegment(Canvas canvas, SolvedCamera camera, Vec3 from, Vec3 to, Func<Vec3, (double X, double Y, double Depth)> at)
    {
        const double near = 0.2;
        double d0 = (from - camera.Position).Dot(camera.Forward), d1 = (to - camera.Position).Dot(camera.Forward);
        if (d0 < near && d1 < near)
            return;
        if (d0 < near)
            from += (to - from) * ((near - d0) / (d1 - d0));
        else if (d1 < near)
            to = from + (to - from) * ((near - d0) / (d1 - d0));
        var a = at(from);
        var b = at(to);
        canvas.Line(a.X, a.Y, b.X, b.Y, 1, FloorLine);
    }
}
