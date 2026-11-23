namespace Bamen.Core.Syntax;

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;
    public bool Contains(int position) => position >= Start && position < End;
    public static TextSpan FromBounds(int start, int end) => new(start, end - start);
}

public enum DiagnosticSeverity { Info, Warning, Error }

/// <summary>
/// One finding, addressed by a source span. Codes are grouped by the layer that found it:
/// BM0xxx syntax, BM1xxx meaning (binding, timing), BM2xxx camera (solving, film grammar).
/// </summary>
public sealed record Diagnostic(string Code, DiagnosticSeverity Severity, TextSpan Span, string Message);
