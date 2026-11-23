namespace Bamen.Core.Syntax;

/// <summary>
/// One statement per line:
/// <code>
/// title "Dance in three shots"
/// dance "dance.shosa"
/// shot 1 wide eye bars 1-4 look alice bob carol
/// </code>
/// After `shot N` the clauses come in any order, each at most once: a size (wide, medium, close),
/// an angle (eye, low, high), a time (`bars A-B`, `beats A-B`, `2s-4s`, `2s`), `look` and its
/// targets, `from` and a side. `look` takes the words after it up to the next clause word.
/// </summary>
internal sealed class Parser
{
    private static readonly Dictionary<string, ShotSize> Sizes = new() { ["wide"] = ShotSize.Wide, ["medium"] = ShotSize.Medium, ["close"] = ShotSize.Close };
    private static readonly Dictionary<string, ShotAngle> Angles = new() { ["eye"] = ShotAngle.Eye, ["low"] = ShotAngle.Low, ["high"] = ShotAngle.High };
    internal static readonly Dictionary<string, ShotSide> Sides = new()
    {
        ["front"] = ShotSide.Front, ["front-left"] = ShotSide.FrontLeft, ["left"] = ShotSide.Left, ["back-left"] = ShotSide.BackLeft,
        ["back"] = ShotSide.Back, ["back-right"] = ShotSide.BackRight, ["right"] = ShotSide.Right, ["front-right"] = ShotSide.FrontRight,
    };

    /// <summary>The words that begin a clause of a shot: `look` stops at them.</summary>
    internal static bool IsClauseWord(string word) =>
        Sizes.ContainsKey(word) || Angles.ContainsKey(word) || word is "bars" or "beats" or "look" or "from";

    private readonly string _text;
    private readonly List<Token> _tokens;
    private readonly List<Diagnostic> _diagnostics = [];
    private int _at;

    private Parser(string text)
    {
        _text = text;
        _tokens = Lexer.Lex(text, _diagnostics);
    }

    public static SyntaxTree Parse(string text) => new Parser(text).ParseFile();

    private Token Current => _tokens[_at];
    private Token Next => _tokens[Math.Min(_at + 1, _tokens.Count - 1)];
    private bool AtLineEnd => Current.Kind is TokenKind.Newline or TokenKind.End;

    private void Error(string code, TextSpan span, string message) =>
        _diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, span, message));

    /// <summary>The span to point at when something is missing: the token there, or the end of the line before it.</summary>
    private TextSpan Here => AtLineEnd ? new TextSpan(_at > 0 ? _tokens[_at - 1].Span.End : 0, 0) : Current.Span;

    private void SkipLine()
    {
        while (!AtLineEnd)
            _at++;
    }

    private SyntaxTree ParseFile()
    {
        var titles = new List<Located<string>>();
        var dances = new List<Located<string>>();
        var shots = new List<ShotSyntax>();
        while (Current.Kind != TokenKind.End)
        {
            if (Current.Kind == TokenKind.Newline)
            {
                _at++;
                continue;
            }
            var first = Current;
            if (first.Kind == TokenKind.Word && first.Text is "title" or "dance")
            {
                _at++;
                if (Current.Kind == TokenKind.String)
                {
                    (first.Text == "title" ? titles : dances).Add(new Located<string>(Current.Text, Current.Span));
                    _at++;
                }
                else
                {
                    Error("BM0002", Here, first.Text == "title" ? "`title` takes a string: title \"My piece\"." : "`dance` takes the Shosa file in quotes: dance \"dance.shosa\".");
                }
                ExpectLineEnd();
            }
            else if (first.Kind == TokenKind.Word && first.Text == "shot")
            {
                if (ParseShot() is { } shot)
                    shots.Add(shot);
            }
            else
            {
                Error("BM0001", first.Span, $"'{first.Text}' does not begin a statement. A line is `title \"…\"`, `dance \"file.shosa\"` or `shot N …`.");
                SkipLine();
            }
        }
        return new SyntaxTree(_text, titles, dances, shots, _diagnostics);
    }

    private void ExpectLineEnd()
    {
        if (AtLineEnd)
            return;
        int start = Current.Span.Start;
        SkipLine();
        Error("BM0005", TextSpan.FromBounds(start, _tokens[_at - 1].Span.End), "Nothing more was expected on this line.");
    }

    private ShotSyntax? ParseShot()
    {
        int start = Current.Span.Start;
        _at++;
        if (Current.Kind != TokenKind.Number || Current.Value != Math.Floor(Current.Value) || Current.Value < 1)
        {
            Error("BM0002", Here, "`shot` is followed by its number, a whole number from 1: shot 1 wide bars 1-4 look alice.");
            SkipLine();
            return null;
        }
        var number = new Located<int>((int)Current.Value, Current.Span);
        _at++;

        Located<ShotSize>? size = null;
        Located<ShotAngle>? angle = null;
        Located<ShotSide>? side = null;
        ShotTime? time = null;
        TextSpan? lookSpan = null;
        var look = new List<LookTarget>();

        bool Once(bool already, Token token, string what)
        {
            if (already)
                Error("BM0003", token.Span, $"This shot already has {what}.");
            return !already;
        }

        while (!AtLineEnd)
        {
            var token = Current;
            if (token.Kind == TokenKind.Word && Sizes.TryGetValue(token.Text, out var s))
            {
                _at++;
                if (Once(size is not null, token, "a size"))
                    size = new Located<ShotSize>(s, token.Span);
            }
            else if (token.Kind == TokenKind.Word && Angles.TryGetValue(token.Text, out var a))
            {
                _at++;
                if (Once(angle is not null, token, "an angle"))
                    angle = new Located<ShotAngle>(a, token.Span);
            }
            else if (token.Kind == TokenKind.Word && token.Text is "bars" or "beats")
            {
                _at++;
                var parsed = ParseCount(token);
                if (parsed is not null && Once(time is not null, token, "a time"))
                    time = parsed;
            }
            else if (token.Kind == TokenKind.Seconds)
            {
                var parsed = ParseSeconds();
                if (Once(time is not null, token, "a time"))
                    time = parsed;
            }
            else if (token.Kind == TokenKind.Word && token.Text == "look")
            {
                _at++;
                var targets = ParseTargets();
                if (targets.Count == 0)
                    Error("BM0002", Here, "`look` is followed by what the shot looks at: a dancer (alice) or a part of one (alice.face).");
                if (Once(lookSpan is not null, token, "a `look`"))
                {
                    lookSpan = TextSpan.FromBounds(token.Span.Start, targets.Count > 0 ? targets[^1].Span.End : token.Span.End);
                    look.AddRange(targets);
                }
            }
            else if (token.Kind == TokenKind.Word && token.Text == "from")
            {
                _at++;
                if (Current.Kind == TokenKind.Word && Sides.TryGetValue(Current.Text, out var d))
                {
                    if (Once(side is not null, token, "a side (`from`)"))
                        side = new Located<ShotSide>(d, TextSpan.FromBounds(token.Span.Start, Current.Span.End));
                    _at++;
                }
                else
                {
                    Error("BM0002", Here, "`from` is followed by a side of the subject: front, back, left, right, front-left, front-right, back-left or back-right.");
                    if (Current.Kind == TokenKind.Word)
                        _at++;
                }
            }
            else
            {
                Error("BM0006", token.Span, $"'{token.Text}' is not part of a shot. A shot takes a size (wide, medium, close), an angle (eye, low, high), a time (bars 1-4, beats 1-8, 2s-4s, 2s), `look` and what it looks at, and `from` and a side.");
                _at++;
            }
        }
        int end = _tokens[_at - 1].Span.End;
        return new ShotSyntax(TextSpan.FromBounds(start, end), number, size, angle, time, lookSpan, look, side);
    }

    /// <summary>`bars 1-4`, `bars 5`, `beats 1-8`: whole numbers from 1.</summary>
    private ShotTime? ParseCount(Token keyword)
    {
        static bool Whole(Token t) => t.Kind == TokenKind.Number && t.Value == Math.Floor(t.Value) && t.Value >= 1;
        if (!Whole(Current))
        {
            Error("BM0002", Here, $"`{keyword.Text}` is followed by a {keyword.Text[..^1]} or a range of them, counted from 1: {keyword.Text} 1-4.");
            return null;
        }
        int first = (int)Current.Value, last = first;
        int end = Current.Span.End;
        _at++;
        if (Current.Kind == TokenKind.Dash)
        {
            _at++;
            if (!Whole(Current))
            {
                Error("BM0002", Here, $"The range needs its last {keyword.Text[..^1]}: {keyword.Text} {first}-{first + 3}.");
                return null;
            }
            last = (int)Current.Value;
            end = Current.Span.End;
            _at++;
        }
        var span = TextSpan.FromBounds(keyword.Span.Start, end);
        return keyword.Text == "bars" ? new BarsTime(span, first, last) : new BeatsTime(span, first, last);
    }

    /// <summary>`2s` (a length) or `2s-4.5s` (from and to).</summary>
    private ShotTime ParseSeconds()
    {
        var from = Current;
        _at++;
        if (Current.Kind == TokenKind.Dash && Next.Kind is TokenKind.Seconds or TokenKind.Number)
        {
            _at++;
            var to = Current;
            _at++;
            if (to.Kind == TokenKind.Number)
                Error("BM0002", to.Span, $"Write the end in seconds too: {from.Text}-{to.Text}s.");
            return new SecondsRange(TextSpan.FromBounds(from.Span.Start, to.Span.End), from.Value, to.Value);
        }
        return new SecondsLength(from.Span, from.Value);
    }

    /// <summary>`alice bob.face carol`, up to the next clause word or the end of the line.</summary>
    private List<LookTarget> ParseTargets()
    {
        var targets = new List<LookTarget>();
        while (Current.Kind == TokenKind.Word && !IsClauseWord(Current.Text))
        {
            var name = new Located<string>(Current.Text, Current.Span);
            _at++;
            Located<string>? part = null;
            if (Current.Kind == TokenKind.Dot)
            {
                _at++;
                if (Current.Kind == TokenKind.Word)
                {
                    part = new Located<string>(Current.Text, Current.Span);
                    _at++;
                }
                else
                {
                    Error("BM0002", Here, $"After '{name.Value}.' comes a part of the body: {name.Value}.face.");
                }
            }
            targets.Add(new LookTarget(TextSpan.FromBounds(name.Span.Start, part?.Span.End ?? name.Span.End), name, part));
        }
        return targets;
    }
}
