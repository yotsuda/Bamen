using System.Globalization;
using Bamen.Core.Semantics;
using Bamen.Core.Syntax;

namespace Bamen.Core.Output;

/// <summary>
/// What `bamen check` says, as lines of text (the language server will hand the same to an AI
/// assistant in the editor, as Shosa's does).
/// </summary>
public static class CheckReport
{
    private static string N(double value) => Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);

    /// <summary>A line for each diagnostic: <c>FILE(line,column): severity CODE: message</c>.</summary>
    public static IEnumerable<string> Problems(BamenCompilation compilation, string path)
    {
        foreach (var d in compilation.Diagnostics)
        {
            var (line, column) = BamenCompilation.LineColumn(compilation.Tree.Text, d.Span.Start);
            yield return $"{path}({line},{column}): {d.Severity.ToString().ToLowerInvariant()} {d.Code}: {d.Message}";
        }
    }

    /// <summary>
    /// <c>FILE: no problems</c> when there is no diagnostic; then the dance (its dancers, length,
    /// tempo and time) and every shot: when it plays in bars and seconds, its size, angle, side and
    /// what it looks at, defaults included, so a writer sees what was understood.
    /// </summary>
    public static IEnumerable<string> Summary(BamenCompilation compilation, string path)
    {
        if (compilation.Diagnostics.Count == 0)
            yield return $"{path}: no problems";
        var bound = compilation.Bound;
        if (bound.Dance is not { } dance)
            yield break;

        int bars = dance.BarAt(dance.TotalBeats - 1e-6);
        string tempo = string.Join(", ", dance.Tempos.Select(t => $"{N(t.Bpm)} bpm" + (t.Beat > 0 ? $" from beat {N(t.Beat + 1)}" : "")));
        string meter = string.Join(", ", dance.Meters.Select(m => $"{m.BeatsPerBar} beats a bar" + (m.Beat > 0 ? $" from beat {N(m.Beat + 1)}" : "")));
        yield return $"  dance {bound.DanceFile}: {dance.Figures.Count} dancers ({string.Join(", ", dance.Figures.Select(f => f.Name))}), "
            + $"{bars} bars = {N(dance.TotalBeats)} beats = {N(dance.TotalSeconds)} s; {tempo}; {meter}"
            + (dance.Music is { } music ? $"; music {music}" : "");

        foreach (var shot in bound.Shots)
        {
            double startBeat = BeatAt(dance, shot.Start), endBeat = BeatAt(dance, shot.End);
            string where = $"{N(shot.Start)}-{N(shot.End)} s ({N(shot.Length)} s), beats {N(startBeat + 1)}-{N(endBeat)}";
            string look = shot.Look.Count > 0 ? string.Join(" ", shot.Look) : "(nothing)";
            yield return $"  shot {shot.Number}: {where}; {Word(shot.Size)} {Word(shot.Angle)} from {Side(shot.Side)}; look {look}";
        }
    }

    /// <summary>The beat (from 0) at a second, through every tempo change: the inverse of <see cref="Dances.Dance.SecondsAt"/>.</summary>
    private static double BeatAt(Dances.Dance dance, double seconds)
    {
        double beat = 0, at = 0;
        for (int i = 0; i < dance.Tempos.Count; i++)
        {
            double length = i + 1 < dance.Tempos.Count ? (dance.Tempos[i + 1].Beat - dance.Tempos[i].Beat) * 60 / dance.Tempos[i].Bpm : double.PositiveInfinity;
            if (seconds <= at + length)
                return dance.Tempos[i].Beat + (seconds - at) * dance.Tempos[i].Bpm / 60;
            at += length;
            beat = dance.Tempos[i + 1].Beat;
        }
        return beat;
    }

    private static string Word<T>(T value) where T : Enum => value.ToString().ToLowerInvariant();

    private static string Side(ShotSide side) => side switch
    {
        ShotSide.FrontLeft => "front-left",
        ShotSide.FrontRight => "front-right",
        ShotSide.BackLeft => "back-left",
        ShotSide.BackRight => "back-right",
        _ => Word(side),
    };
}
