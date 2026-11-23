using Bamen.Core.Dances;
using Bamen.Core.Semantics;
using Bamen.Core.Syntax;

namespace Bamen.Core;

/// <summary>
/// The pipeline in one place: text -> SyntaxTree -> BoundFile (the dance read, the shots in
/// seconds). The camera solver comes after (ROADMAP M0, step 3).
/// </summary>
public sealed class BamenCompilation
{
    private BamenCompilation(SyntaxTree tree, BoundFile bound, List<Diagnostic> diagnostics)
    {
        Tree = tree;
        Bound = bound;
        Diagnostics = diagnostics;
    }

    public SyntaxTree Tree { get; }
    public BoundFile Bound { get; }

    /// <summary>Syntax and meaning diagnostics together, in source order.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <param name="dances">Reads the file the `dance` line names (<see cref="ShosaClipReader.Read"/> on disk). Without one no dance is read.</param>
    /// <param name="folder">The folder of the .bamen file, which the `dance` path is relative to.</param>
    public static BamenCompilation Compile(string text, DanceSource? dances = null, string? folder = null)
    {
        var tree = SyntaxTree.Parse(text);
        var diagnostics = new List<Diagnostic>(tree.Diagnostics);
        var bound = Binder.Bind(tree, dances, folder, diagnostics);
        return new BamenCompilation(tree, bound, diagnostics.OrderBy(d => d.Span.Start).ToList());
    }

    /// <summary>1-based line and column of a position in the text.</summary>
    public static (int Line, int Column) LineColumn(string text, int position)
    {
        int line = 1, column = 1;
        for (int i = 0; i < position && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }
        return (line, column);
    }
}
