using Bamen.Core.Cameras;

namespace Bamen.Core.Dances;

/// <summary>One dancer of a dance: every joint at every frame, x y z in metres (y up, the audience toward +z).</summary>
public sealed record DanceFigure(string Name, IReadOnlyList<float[]> Frames)
{
    /// <summary>A joint at a frame; a frame before the first or after the last is the first or the last.</summary>
    public Vec3 Joint(int frame, int joint)
    {
        var f = Frames[Math.Clamp(frame, 0, Frames.Count - 1)];
        return new Vec3(f[joint * 3], f[joint * 3 + 1], f[joint * 3 + 2]);
    }
}

/// <summary>A bone to draw between two joints, and the side it is on: c (the middle), l or r.</summary>
public readonly record struct DanceBone(int A, int B, char Side);

/// <summary>A tempo from a beat on (beats counted from 0).</summary>
public readonly record struct TempoChange(double Beat, double Bpm);

/// <summary>A time signature from a beat on (beats counted from 0).</summary>
public readonly record struct MeterChange(double Beat, int BeatsPerBar);

/// <summary>
/// The movement a `dance` line brings in: what `shosa clip` writes (DESIGN.md 3.3), the parts
/// Bamen uses. Time is kept in beats as Shosa keeps it; <see cref="SecondsAt"/> and
/// <see cref="BeatAtBar"/> turn bars and beats into seconds.
/// </summary>
public sealed class Dance
{
    public Dance(string title, double fps, double totalBeats, IReadOnlyList<TempoChange> tempos, IReadOnlyList<MeterChange> meters,
        IReadOnlyList<string> joints, IReadOnlyList<DanceFigure> figures, string? music, IReadOnlyList<DanceBone>? bones = null)
    {
        Bones = bones ?? [];
        Title = title;
        Fps = fps;
        TotalBeats = totalBeats;
        Tempos = tempos.Count > 0 ? tempos.OrderBy(t => t.Beat).ToList() : [new TempoChange(0, 120)];
        Meters = meters.Count > 0 ? meters.OrderBy(m => m.Beat).ToList() : [new MeterChange(0, 4)];
        Joints = joints;
        Figures = figures;
        Music = music;
    }

    /// <summary>
    /// The clip as `shosa clip` wrote it (the one score shot), JSON: the preview draws the dancers from it
    /// as Shosa's own preview does. Null when the dance was not read from a clip (a test's).
    /// </summary>
    public string? ClipJson { get; init; }

    public string Title { get; }
    public double Fps { get; }
    public double TotalBeats { get; }
    public IReadOnlyList<TempoChange> Tempos { get; }
    public IReadOnlyList<MeterChange> Meters { get; }
    public IReadOnlyList<string> Joints { get; }
    public IReadOnlyList<DanceFigure> Figures { get; }

    /// <summary>The bones to draw (`bones` and `boneSides` of the clip); empty when the clip has none.</summary>
    public IReadOnlyList<DanceBone> Bones { get; }

    /// <summary>
    /// The frames from one second up to another (frames are evenly spaced in time): the first at or
    /// after <paramref name="from"/>, the last before <paramref name="to"/> (the frame on it is the next
    /// shot's), kept inside the dance. At least one.
    /// </summary>
    public (int First, int Last) FramesOf(double from, double to)
    {
        int end = Math.Max(0, (Figures.Count > 0 ? Figures[0].Frames.Count : 1) - 1);
        int first = Math.Clamp((int)Math.Ceiling(from * Fps - 1e-6), 0, end);
        int last = Math.Clamp((int)Math.Ceiling(to * Fps - 1e-6) - 1, first, end);
        return (first, last);
    }

    /// <summary>The song the dance is danced to (`music` in the Shosa file), or null.</summary>
    public string? Music { get; }

    /// <summary>The Shosa file the dance was read from, in full; null when it was not read from a file.</summary>
    public string? File { get; init; }

    public double TotalSeconds => SecondsAt(TotalBeats);

    /// <summary>Seconds from the start at a beat (from 0), through every tempo change.</summary>
    public double SecondsAt(double beat)
    {
        double seconds = 0;
        for (int i = 0; i < Tempos.Count; i++)
        {
            double from = Tempos[i].Beat;
            if (beat <= from)
                break;
            double to = i + 1 < Tempos.Count ? Math.Min(beat, Tempos[i + 1].Beat) : beat;
            seconds += (to - from) * 60 / Tempos[i].Bpm;
        }
        return seconds;
    }

    /// <summary>The beat (from 0) at a second, through every tempo change: the inverse of <see cref="SecondsAt"/>.</summary>
    public double BeatAt(double seconds)
    {
        double beat = 0, at = 0;
        for (int i = 0; i < Tempos.Count; i++)
        {
            double length = i + 1 < Tempos.Count ? (Tempos[i + 1].Beat - Tempos[i].Beat) * 60 / Tempos[i].Bpm : double.PositiveInfinity;
            if (seconds <= at + length)
                return Tempos[i].Beat + (seconds - at) * Tempos[i].Bpm / 60;
            at += length;
            beat = Tempos[i + 1].Beat;
        }
        return beat;
    }

    /// <summary>The beat (from 0) at which a bar (from 1) begins, through every change of time. Bar 1 begins at beat 0.</summary>
    public double BeatAtBar(int bar)
    {
        double beat = 0;
        int counted = 1;
        int m = 0;
        while (counted < bar)
        {
            while (m + 1 < Meters.Count && Meters[m + 1].Beat <= beat + 1e-9)
                m++;
            beat += Meters[m].BeatsPerBar;
            counted++;
        }
        return beat;
    }

    /// <summary>The bar (from 1) a beat (from 0) falls in.</summary>
    public int BarAt(double beat)
    {
        int bar = 1;
        while (BeatAtBar(bar + 1) <= beat + 1e-9)
            bar++;
        return bar;
    }

    public int JointIndex(string name) => Joints is List<string> list ? list.IndexOf(name) : Joints.ToList().IndexOf(name);
}
