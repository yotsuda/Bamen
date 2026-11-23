namespace Bamen.Core.Dances;

/// <summary>One dancer of a dance: every joint at every frame, x y z in metres (y up).</summary>
public sealed record DanceFigure(string Name, IReadOnlyList<float[]> Frames);

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
        IReadOnlyList<string> joints, IReadOnlyList<DanceFigure> figures, string? music)
    {
        Title = title;
        Fps = fps;
        TotalBeats = totalBeats;
        Tempos = tempos.Count > 0 ? tempos.OrderBy(t => t.Beat).ToList() : [new TempoChange(0, 120)];
        Meters = meters.Count > 0 ? meters.OrderBy(m => m.Beat).ToList() : [new MeterChange(0, 4)];
        Joints = joints;
        Figures = figures;
        Music = music;
    }

    public string Title { get; }
    public double Fps { get; }
    public double TotalBeats { get; }
    public IReadOnlyList<TempoChange> Tempos { get; }
    public IReadOnlyList<MeterChange> Meters { get; }
    public IReadOnlyList<string> Joints { get; }
    public IReadOnlyList<DanceFigure> Figures { get; }

    /// <summary>The song the dance is danced to (`music` in the Shosa file), or null.</summary>
    public string? Music { get; }

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
