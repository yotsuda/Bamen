using System.Globalization;

namespace Bamen.Core.Syntax;

public enum TokenKind { Word, Number, Seconds, String, Dash, Dot, Newline, Bad, End }

/// <summary>
/// A token. <see cref="Value"/> is the number for Number and Seconds, <see cref="Text"/> the word
/// or the string's contents (without the quotes).
/// </summary>
public readonly record struct Token(TokenKind Kind, TextSpan Span, string Text, double Value = 0);

/// <summary>
/// Splits a .bamen file into tokens. A statement is one line, so line ends are tokens. `//` starts a
/// comment to the end of the line. A word begins with a letter (any script: 花子 is a word) and may
/// hold letters, digits, `_` and inner `-` (`front-left`). A number directly followed by `s` is
/// seconds (`2s`, `1.5s`).
/// </summary>
public static class Lexer
{
    public static List<Token> Lex(string text, List<Diagnostic> diagnostics)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\n')
            {
                tokens.Add(new Token(TokenKind.Newline, new TextSpan(i, 1), "\n"));
                i++;
            }
            else if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                    i++;
            }
            else if (c == '"')
            {
                int start = i++;
                while (i < text.Length && text[i] != '"' && text[i] != '\n')
                    i++;
                if (i < text.Length && text[i] == '"')
                {
                    tokens.Add(new Token(TokenKind.String, TextSpan.FromBounds(start, i + 1), text[(start + 1)..i]));
                    i++;
                }
                else
                {
                    diagnostics.Add(new Diagnostic("BM0004", DiagnosticSeverity.Error, TextSpan.FromBounds(start, i), "The string is not closed: end it with \" on the same line."));
                    tokens.Add(new Token(TokenKind.String, TextSpan.FromBounds(start, i), text[(start + 1)..i]));
                }
            }
            else if (char.IsAsciiDigit(c))
            {
                int start = i;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                    i++;
                if (i + 1 < text.Length && text[i] == '.' && char.IsAsciiDigit(text[i + 1]))
                {
                    i++;
                    while (i < text.Length && char.IsAsciiDigit(text[i]))
                        i++;
                }
                double value = double.Parse(text.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
                if (i < text.Length && text[i] == 's' && !(i + 1 < text.Length && IsWordPart(text[i + 1])))
                {
                    i++;
                    tokens.Add(new Token(TokenKind.Seconds, TextSpan.FromBounds(start, i), text[start..i], value));
                }
                else
                {
                    tokens.Add(new Token(TokenKind.Number, TextSpan.FromBounds(start, i), text[start..i], value));
                }
            }
            else if (char.IsLetter(c) || c == '_')
            {
                int start = i++;
                while (i < text.Length && (IsWordPart(text[i]) || (text[i] == '-' && i + 1 < text.Length && char.IsLetter(text[i + 1]))))
                    i++;
                tokens.Add(new Token(TokenKind.Word, TextSpan.FromBounds(start, i), text[start..i]));
            }
            else if (c == '-')
            {
                tokens.Add(new Token(TokenKind.Dash, new TextSpan(i, 1), "-"));
                i++;
            }
            else if (c == '.')
            {
                tokens.Add(new Token(TokenKind.Dot, new TextSpan(i, 1), "."));
                i++;
            }
            else
            {
                tokens.Add(new Token(TokenKind.Bad, new TextSpan(i, 1), c.ToString()));
                i++;
            }
        }
        tokens.Add(new Token(TokenKind.End, new TextSpan(text.Length, 0), ""));
        return tokens;
    }

    private static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || c == '_';
}
