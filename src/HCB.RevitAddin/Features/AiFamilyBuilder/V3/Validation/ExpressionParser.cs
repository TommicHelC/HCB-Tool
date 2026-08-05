using System;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Validation;

/// <summary>
/// Ograniczony parser wyrazen dla pola <c>expr</c>. Dopuszcza wylacznie:
/// liczby, nazwy parametrow (litera/podkreslnik, dalej litera/cyfra/podkreslnik),
/// operatory + - * /, unarny minus oraz nawiasy.
/// Nie dopuszcza funkcji, dowolnego kodu ani niejawnych jednostek.
/// Czysty C# bez referencji do Revita (gotowy do testow jednostkowych).
/// </summary>
public static class ExpressionParser
{
    /// <summary>
    /// Sprawdza skladnie wyrazenia i zwraca zbior uzytych identyfikatorow (nazw parametrow).
    /// </summary>
    public static bool TryValidate(string? expression, out IReadOnlyCollection<string> identifiers, out string? error)
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        identifiers = ids;

        if (!TryParse(expression, out ExpressionNode? root, out error) || root == null)
        {
            return false;
        }

        root.CollectIdentifiers(ids);
        return true;
    }

    /// <summary>
    /// Oblicza wartosc wyrazenia. <paramref name="resolve"/> zwraca wartosc parametru lub null, gdy nieznany.
    /// </summary>
    public static bool TryEvaluate(string? expression, Func<string, double?> resolve, out double value, out string? error)
    {
        value = 0;
        if (!TryParse(expression, out ExpressionNode? root, out error) || root == null)
        {
            return false;
        }

        try
        {
            value = root.Evaluate(resolve);
            return true;
        }
        catch (ExpressionException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryParse(string? expression, out ExpressionNode? root, out string? error)
    {
        root = null;
        error = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Wyrazenie jest puste.";
            return false;
        }

        try
        {
            List<Token> tokens = Tokenize(expression);
            Parser parser = new(tokens);
            root = parser.ParseExpression();
            parser.ExpectEnd();
            return true;
        }
        catch (ExpressionException ex)
        {
            error = ex.Message;
            root = null;
            return false;
        }
    }

    private static List<Token> Tokenize(string expression)
    {
        List<Token> tokens = [];
        int i = 0;
        while (i < expression.Length)
        {
            char c = expression[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            switch (c)
            {
                case '+':
                case '-':
                case '*':
                case '/':
                case '(':
                case ')':
                    tokens.Add(new Token(MapOperator(c), c.ToString()));
                    i++;
                    continue;
            }

            if (char.IsDigit(c) || c == '.')
            {
                int start = i;
                bool seenDot = false;
                while (i < expression.Length && (char.IsDigit(expression[i]) || expression[i] == '.'))
                {
                    if (expression[i] == '.')
                    {
                        if (seenDot)
                        {
                            throw new ExpressionException($"Niepoprawna liczba w pozycji {start}.");
                        }

                        seenDot = true;
                    }

                    i++;
                }

                string numberText = expression.Substring(start, i - start);
                if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    throw new ExpressionException($"Niepoprawna liczba: '{numberText}'.");
                }

                tokens.Add(new Token(TokenKind.Number, numberText, number));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Identifier, expression.Substring(start, i - start)));
                continue;
            }

            throw new ExpressionException($"Niedozwolony znak '{c}' w pozycji {i}.");
        }

        return tokens;
    }

    private static TokenKind MapOperator(char c) => c switch
    {
        '+' => TokenKind.Plus,
        '-' => TokenKind.Minus,
        '*' => TokenKind.Star,
        '/' => TokenKind.Slash,
        '(' => TokenKind.LParen,
        ')' => TokenKind.RParen,
        _ => throw new ExpressionException($"Nieobslugiwany operator '{c}'.")
    };

    private enum TokenKind
    {
        Number,
        Identifier,
        Plus,
        Minus,
        Star,
        Slash,
        LParen,
        RParen
    }

    private readonly record struct Token(TokenKind Kind, string Text, double Number = 0);

    private sealed class Parser(List<Token> tokens)
    {
        private int _position;

        public ExpressionNode ParseExpression()
        {
            ExpressionNode left = ParseTerm();
            while (TryPeek(out Token token) && token.Kind is TokenKind.Plus or TokenKind.Minus)
            {
                _position++;
                ExpressionNode right = ParseTerm();
                left = new BinaryNode(token.Kind == TokenKind.Plus ? '+' : '-', left, right);
            }

            return left;
        }

        private ExpressionNode ParseTerm()
        {
            ExpressionNode left = ParseFactor();
            while (TryPeek(out Token token) && token.Kind is TokenKind.Star or TokenKind.Slash)
            {
                _position++;
                ExpressionNode right = ParseFactor();
                left = new BinaryNode(token.Kind == TokenKind.Star ? '*' : '/', left, right);
            }

            return left;
        }

        private ExpressionNode ParseFactor()
        {
            if (!TryPeek(out Token token))
            {
                throw new ExpressionException("Nieoczekiwany koniec wyrazenia.");
            }

            switch (token.Kind)
            {
                case TokenKind.Minus:
                    _position++;
                    return new UnaryMinusNode(ParseFactor());

                case TokenKind.Plus:
                    _position++;
                    return ParseFactor();

                case TokenKind.Number:
                    _position++;
                    return new NumberNode(token.Number);

                case TokenKind.Identifier:
                    _position++;
                    return new IdentifierNode(token.Text);

                case TokenKind.LParen:
                    _position++;
                    ExpressionNode inner = ParseExpression();
                    Expect(TokenKind.RParen, "Brak zamykajacego nawiasu ')'.");
                    return inner;

                default:
                    throw new ExpressionException($"Nieoczekiwany token '{token.Text}'.");
            }
        }

        public void ExpectEnd()
        {
            if (_position != tokens.Count)
            {
                throw new ExpressionException($"Nieoczekiwany token '{tokens[_position].Text}' po wyrazeniu.");
            }
        }

        private void Expect(TokenKind kind, string message)
        {
            if (!TryPeek(out Token token) || token.Kind != kind)
            {
                throw new ExpressionException(message);
            }

            _position++;
        }

        private bool TryPeek(out Token token)
        {
            if (_position < tokens.Count)
            {
                token = tokens[_position];
                return true;
            }

            token = default;
            return false;
        }
    }
}

public sealed class ExpressionException(string message) : Exception(message);

public abstract class ExpressionNode
{
    public abstract double Evaluate(Func<string, double?> resolve);

    public abstract void CollectIdentifiers(ISet<string> identifiers);
}

public sealed class NumberNode(double value) : ExpressionNode
{
    public override double Evaluate(Func<string, double?> resolve) => value;

    public override void CollectIdentifiers(ISet<string> identifiers)
    {
    }
}

public sealed class IdentifierNode(string name) : ExpressionNode
{
    public string Name { get; } = name;

    public override double Evaluate(Func<string, double?> resolve)
    {
        double? resolved = resolve(Name);
        if (!resolved.HasValue)
        {
            throw new ExpressionException($"Nieznany parametr w wyrazeniu: '{Name}'.");
        }

        return resolved.Value;
    }

    public override void CollectIdentifiers(ISet<string> identifiers) => identifiers.Add(Name);
}

public sealed class UnaryMinusNode(ExpressionNode operand) : ExpressionNode
{
    public override double Evaluate(Func<string, double?> resolve) => -operand.Evaluate(resolve);

    public override void CollectIdentifiers(ISet<string> identifiers) => operand.CollectIdentifiers(identifiers);
}

public sealed class BinaryNode(char op, ExpressionNode left, ExpressionNode right) : ExpressionNode
{
    public override double Evaluate(Func<string, double?> resolve)
    {
        double l = left.Evaluate(resolve);
        double r = right.Evaluate(resolve);
        return op switch
        {
            '+' => l + r,
            '-' => l - r,
            '*' => l * r,
            '/' => r == 0 ? throw new ExpressionException("Dzielenie przez zero w wyrazeniu.") : l / r,
            _ => throw new ExpressionException($"Nieobslugiwany operator '{op}'.")
        };
    }

    public override void CollectIdentifiers(ISet<string> identifiers)
    {
        left.CollectIdentifiers(identifiers);
        right.CollectIdentifiers(identifiers);
    }
}
