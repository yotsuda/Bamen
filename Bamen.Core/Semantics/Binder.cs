using System.Globalization;
using Bamen.Core.Dances;
using Bamen.Core.Syntax;

namespace Bamen.Core.Semantics;

/// <summary>A target a shot looks at, bound: a dancer of the dance and the joints it means.</summary>
public sealed record BoundTarget(string Figure, string? Part, IReadOnlyList<int> Joints, TextSpan Span)
{
    public override string ToString() => Part is null ? Figure : $"{Figure}.{Part}";
}

/// <summary>A shot as the camera solver is given it: everything said or defaulted, time in seconds.</summary>
public sealed record BoundShot(
    int Number,
    ShotSize Size,
    ShotAngle Angle,
    ShotSide Side,
    double Start,
    double End,
    IReadOnlyList<BoundTarget> Look,
    ShotSyntax Syntax)
{
    public double Length => End - Start;
}

/// <summary>What the binder made of a file: the dance it shoots and its shots, in source order.</summary>
public sealed record BoundFile(string? Title, Dance? Dance, string? DanceFile, IReadOnlyList<BoundShot> Shots);

/// <summary>
/// Gives the parsed file its meaning: reads the dance, turns bars and beats into seconds, finds the
/// dancers and parts `look` names, and says what does not add up (BM1xxx).
/// </summary>
public static class Binder
{
    /// <summary>The parts of a dancer a shot can look at, and the joints of the Shosa skeleton each means.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Parts = new Dictionary<string, string[]>
    {
        ["face"] = ["head", "nose"],
        ["head"] = ["head"],
        ["chest"] = ["chest"],
        ["hips"] = ["pelvis"],
        ["hands"] = ["leftHand", "rightHand"],
        ["leftHand"] = ["leftHand"],
        ["rightHand"] = ["rightHand"],
        ["feet"] = ["leftAnkle", "leftToe", "rightAnkle", "rightToe"],
    };

    private const double Tolerance = 1e-6;

    private static string N(double value) => Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);

    public static BoundFile Bind(SyntaxTree tree, DanceSource? dances, string? folder, List<Diagnostic> diagnostics)
    {
        void Say(string code, DiagnosticSeverity severity, TextSpan span, string message) =>
            diagnostics.Add(new Diagnostic(code, severity, span, message));

        foreach (var extra in tree.Titles.Skip(1))
            Say("BM1003", DiagnosticSeverity.Warning, extra.Span, "The file already has a title; this one is not used.");
        foreach (var extra in tree.Dances.Skip(1))
            Say("BM1003", DiagnosticSeverity.Error, extra.Span, "The file already has a `dance`. M0 shoots one dance per file.");

        Dance? dance = null;
        string? danceFile = null;
        if (tree.Dances.Count == 0)
        {
            Say("BM1001", DiagnosticSeverity.Error, new TextSpan(0, 0), "There is no `dance` line: name the Shosa file to shoot, dance \"dance.shosa\".");
        }
        else if (dances is not null)
        {
            var line = tree.Dances[0];
            danceFile = line.Value;
            var load = dances(line.Value, folder);
            dance = load.Dance;
            if (load.Problem is not null)
                Say("BM1002", dance is null ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning, line.Span, load.Problem);
        }

        var shots = new List<BoundShot>();
        var numbers = new Dictionary<int, ShotSyntax>();
        int lastNumber = 0;
        double? previousEnd = null;
        ShotSyntax? previous = null;
        foreach (var shot in tree.Shots)
        {
            if (numbers.TryGetValue(shot.Number.Value, out var before))
                Say("BM1010", DiagnosticSeverity.Error, shot.Number.Span, $"There is already a shot {shot.Number.Value} (line {BamenCompilation.LineColumn(tree.Text, before.Span.Start).Line}).");
            else
                numbers[shot.Number.Value] = shot;
            if (shot.Number.Value < lastNumber && numbers[shot.Number.Value] == shot)
                Say("BM1011", DiagnosticSeverity.Warning, shot.Number.Span, $"Shot {shot.Number.Value} comes after shot {lastNumber}: number the shots in the order they play.");
            lastNumber = Math.Max(lastNumber, shot.Number.Value);

            if (shot.Size is null)
                Say("BM1004", DiagnosticSeverity.Error, shot.Number.Span, $"Shot {shot.Number.Value} has no size: wide, medium or close.");
            if (shot.Time is null)
                Say("BM1005", DiagnosticSeverity.Error, shot.Number.Span, $"Shot {shot.Number.Value} has no time: bars 1-4, beats 1-8, 2s-4s, or a length (2s).");
            if (shot.LookSpan is null)
                Say("BM1006", DiagnosticSeverity.Error, shot.Number.Span, $"Shot {shot.Number.Value} looks at nothing: look alice, or look alice.face.");

            var targets = dance is null ? [] : BindTargets(shot, dance, Say);
            var span = Time(shot, dance, previousEnd, Say);

            if (span is { } t)
            {
                if (previousEnd is { } end && previous is not null && shot.Time is not SecondsLength)
                {
                    if (t.Start > end + Tolerance)
                        Say("BM1013", DiagnosticSeverity.Warning, shot.Time!.Span, $"Nothing is shot from {N(end)} s to {N(t.Start)} s, between shot {previous.Number.Value} and this one.");
                    else if (t.Start < end - Tolerance)
                        Say("BM1014", DiagnosticSeverity.Warning, shot.Time!.Span, $"This shot begins at {N(t.Start)} s, before shot {previous.Number.Value} ends ({N(end)} s).");
                }
                else if (previous is null && t.Start > Tolerance)
                {
                    Say("BM1013", DiagnosticSeverity.Warning, shot.Time!.Span, $"Nothing is shot from the start to {N(t.Start)} s, before this first shot.");
                }
                if (dance is not null && t.End > dance.TotalSeconds + 1e-3)
                    Say("BM1015", DiagnosticSeverity.Warning, shot.Time!.Span, $"The dance ends at {N(dance.TotalSeconds)} s (bar {dance.BarAt(dance.TotalBeats - Tolerance)}); this shot runs to {N(t.End)} s.");
                previousEnd = t.End;
                previous = shot;
            }

            if (span is { } s && shot.Size is not null)
                shots.Add(new BoundShot(shot.Number.Value, shot.Size.Value.Value, shot.Angle?.Value ?? ShotAngle.Eye, shot.Side?.Value ?? ShotSide.Front,
                    s.Start, s.End, targets, shot));
        }

        if (dance is not null && previous is not null && previousEnd is { } last && last < dance.TotalSeconds - 1e-3)
            Say("BM1016", DiagnosticSeverity.Info, previous.Time!.Span, $"The dance goes on to {N(dance.TotalSeconds)} s; the shots end at {N(last)} s.");

        return new BoundFile(tree.Titles.Count > 0 ? tree.Titles[0].Value : null, dance, danceFile, shots);
    }

    private static List<BoundTarget> BindTargets(ShotSyntax shot, Dance dance, Action<string, DiagnosticSeverity, TextSpan, string> say)
    {
        var targets = new List<BoundTarget>();
        var names = dance.Figures.Select(f => f.Name).ToList();
        foreach (var target in shot.Look)
        {
            if (!names.Contains(target.Name.Value))
            {
                say("BM1007", DiagnosticSeverity.Error, target.Name.Span, $"The dance has no dancer '{target.Name.Value}'. Its dancers: {string.Join(", ", names)}.");
                continue;
            }
            IReadOnlyList<int> joints;
            if (target.Part is { } part)
            {
                if (!Parts.TryGetValue(part.Value, out var jointNames))
                {
                    say("BM1008", DiagnosticSeverity.Error, part.Span, $"'{part.Value}' is not a part a shot can look at: {string.Join(", ", Parts.Keys)}.");
                    continue;
                }
                joints = jointNames.Select(dance.JointIndex).Where(i => i >= 0).ToList();
            }
            else
            {
                joints = Enumerable.Range(0, dance.Joints.Count).ToList();
            }
            if (targets.Any(t => t.Figure == target.Name.Value && t.Part == target.Part?.Value))
            {
                string written = target.Part is { } p ? $"{target.Name.Value}.{p.Value}" : target.Name.Value;
                say("BM1009", DiagnosticSeverity.Warning, target.Span, $"'{written}' is already in this `look`.");
                continue;
            }
            targets.Add(new BoundTarget(target.Name.Value, target.Part?.Value, joints, target.Span));
        }
        return targets;
    }

    /// <summary>When the shot plays, in seconds from the start of the dance; null when it cannot be said.</summary>
    private static (double Start, double End)? Time(ShotSyntax shot, Dance? dance, double? previousEnd, Action<string, DiagnosticSeverity, TextSpan, string> say)
    {
        switch (shot.Time)
        {
            case BarsTime bars:
                if (bars.Last < bars.First)
                {
                    say("BM1012", DiagnosticSeverity.Error, bars.Span, $"The range goes backwards: bars {bars.Last}-{bars.First}?");
                    return null;
                }
                return dance is null ? null : (dance.SecondsAt(dance.BeatAtBar(bars.First)), dance.SecondsAt(dance.BeatAtBar(bars.Last + 1)));
            case BeatsTime beats:
                if (beats.Last < beats.First)
                {
                    say("BM1012", DiagnosticSeverity.Error, beats.Span, $"The range goes backwards: beats {beats.Last}-{beats.First}?");
                    return null;
                }
                return dance is null ? null : (dance.SecondsAt(beats.First - 1), dance.SecondsAt(beats.Last));
            case SecondsRange range:
                if (range.To <= range.From)
                {
                    say("BM1012", DiagnosticSeverity.Error, range.Span, "The shot must end after it begins.");
                    return null;
                }
                return (range.From, range.To);
            case SecondsLength length:
                if (length.Length <= 0)
                {
                    say("BM1012", DiagnosticSeverity.Error, length.Span, "A shot lasts longer than 0 s.");
                    return null;
                }
                double start = previousEnd ?? 0;
                return (start, start + length.Length);
            default:
                return null;
        }
    }
}
