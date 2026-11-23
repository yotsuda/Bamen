namespace Bamen.Core.Syntax;

/// <summary>A value as written, with where it was written.</summary>
public readonly record struct Located<T>(T Value, TextSpan Span);

public enum ShotSize { Wide, Medium, Close }

public enum ShotAngle { Eye, Low, High }

/// <summary>
/// Which side of the subject the camera stands on, in the subject's own directions:
/// `from left` is on the subject's left.
/// </summary>
public enum ShotSide { Front, FrontLeft, Left, BackLeft, Back, BackRight, Right, FrontRight }

/// <summary>When a shot plays, as written.</summary>
public abstract record ShotTime(TextSpan Span);

/// <summary>`bars 1-4` or `bars 5`: from the start of the first bar to the end of the last (1-based).</summary>
public sealed record BarsTime(TextSpan Span, int First, int Last) : ShotTime(Span);

/// <summary>`beats 1-16`: from the start of the first beat to the end of the last (1-based).</summary>
public sealed record BeatsTime(TextSpan Span, int First, int Last) : ShotTime(Span);

/// <summary>`2s-4.5s`: from and to, in seconds from the start of the dance.</summary>
public sealed record SecondsRange(TextSpan Span, double From, double To) : ShotTime(Span);

/// <summary>`2s`: this long, from where the shot before ended (the start, for the first).</summary>
public sealed record SecondsLength(TextSpan Span, double Length) : ShotTime(Span);

/// <summary>What a shot looks at: a dancer, or a part of one (`alice.face`).</summary>
public sealed record LookTarget(TextSpan Span, Located<string> Name, Located<string>? Part);

public sealed record ShotSyntax(
    TextSpan Span,
    Located<int> Number,
    Located<ShotSize>? Size,
    Located<ShotAngle>? Angle,
    ShotTime? Time,
    TextSpan? LookSpan,
    IReadOnlyList<LookTarget> Look,
    Located<ShotSide>? Side);

/// <summary>A parsed .bamen file: what it says, as written, in source order.</summary>
public sealed class SyntaxTree
{
    internal SyntaxTree(string text, IReadOnlyList<Located<string>> titles, IReadOnlyList<Located<string>> dances, IReadOnlyList<ShotSyntax> shots, IReadOnlyList<Diagnostic> diagnostics)
    {
        Text = text;
        Titles = titles;
        Dances = dances;
        Shots = shots;
        Diagnostics = diagnostics;
    }

    public string Text { get; }

    /// <summary>Every `title` line (one is expected).</summary>
    public IReadOnlyList<Located<string>> Titles { get; }

    /// <summary>Every `dance` line: the file it names, with the span of the string.</summary>
    public IReadOnlyList<Located<string>> Dances { get; }

    public IReadOnlyList<ShotSyntax> Shots { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public static SyntaxTree Parse(string text) => Parser.Parse(text);
}
