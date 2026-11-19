using System.Globalization;
using System.Text.RegularExpressions;
using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Search.Filtering;

internal enum LiteralKind { Null, Boolean, Number, String, DateTimeOffset }

internal sealed record Literal(LiteralKind Kind, object? Value);

// A field path from the document root (Variable null) or from a lambda's range variable.
internal sealed record FieldRef(string? Variable, string[] Segments, string Path, string Type, SearchField? Field, List<SearchField>? SubFields)
{
    public bool IsCollection => Type.StartsWith("Collection(", StringComparison.Ordinal);

    public string ElementType => IsCollection ? Type["Collection(".Length..^1] : Type;
}

internal abstract record FilterNode;

internal sealed record AndNode(FilterNode Left, FilterNode Right) : FilterNode;

internal sealed record OrNode(FilterNode Left, FilterNode Right) : FilterNode;

internal sealed record NotNode(FilterNode Operand) : FilterNode;

internal sealed record ConstantNode(bool Value) : FilterNode;

internal sealed record BooleanFieldNode(FieldRef Field) : FilterNode;

internal sealed record ComparisonNode(FieldRef Field, string Operator, Literal Value) : FilterNode;

internal sealed record LambdaNode(FieldRef Collection, bool All, string? Variable, FilterNode? Body) : FilterNode;

internal sealed record SearchInNode(FieldRef Field, HashSet<string> Values) : FilterNode;

internal sealed record IsMatchNode(int Index, string Search, string? SearchFields, string QueryType, string SearchMode) : FilterNode;

internal sealed record ParsedFilter(FilterNode Root, IReadOnlyList<IsMatchNode> IsMatches);

/// <summary>
/// Recursive-descent parser for the Azure AI Search subset of OData $filter. Anything it can't
/// evaluate exactly (unknown fields or functions, type mismatches, constructs Azure rejects) throws
/// ArgumentException, which the API returns as 400, so no clause is ever silently ignored.
/// </summary>
internal sealed class ODataFilterParser
{
    private enum TokenKind { Identifier, String, Number, DateTimeOffset, OpenParen, CloseParen, Comma, Colon, Slash, End }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    private abstract record Operand;

    private sealed record FieldOperand(FieldRef Field) : Operand;

    private sealed record LiteralOperand(Literal Literal) : Operand;

    private sealed record NodeOperand(FilterNode Node) : Operand;

    private static readonly HashSet<string> ComparisonOperators = new(StringComparer.Ordinal) { "eq", "ne", "gt", "ge", "lt", "le" };

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal) { "and", "or", "not", "eq", "ne", "gt", "ge", "lt", "le", "any", "all", "true", "false", "null" };

    private static readonly Regex DateTimeLiteral = new(
        @"\G\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})", RegexOptions.CultureInvariant);

    private static readonly Regex NumberLiteral = new(@"\G-?\d+(\.\d+)?([eE][+-]?\d+)?", RegexOptions.CultureInvariant);

    private static readonly Regex Identifier = new(@"\G[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*", RegexOptions.CultureInvariant);

    private readonly SearchIndex _schema;
    private readonly string _filter;
    private readonly List<Token> _tokens;
    private readonly Dictionary<string, FieldRef> _variables = new(StringComparer.Ordinal);
    private readonly Stack<string> _scopes = new();
    private readonly List<IsMatchNode> _isMatches = [];
    private int _position;

    private ODataFilterParser(SearchIndex schema, string filter)
    {
        _schema = schema;
        _filter = filter;
        _tokens = Tokenize(filter);
    }

    public static ParsedFilter Parse(SearchIndex schema, string filter)
    {
        var parser = new ODataFilterParser(schema, filter);
        var root = parser.ParseOr();
        if (parser.Peek.Kind != TokenKind.End)
        {
            throw parser.Error($"unexpected '{parser.Peek.Text}'", parser.Peek);
        }

        return new ParsedFilter(root, parser._isMatches);
    }

    private Token Peek => _tokens[_position];

    private Token Next() => _tokens[_position++];

    private bool PeekKeyword(string keyword) => Peek.Kind == TokenKind.Identifier && Peek.Text == keyword;

    private Token Expect(TokenKind kind, string description)
    {
        var token = Next();
        return token.Kind == kind ? token : throw Error($"expected {description} but found '{token.Text}'", token);
    }

    private FilterNode ParseOr()
    {
        var left = ParseAnd();
        while (PeekKeyword("or"))
        {
            Next();
            left = new OrNode(left, ParseAnd());
        }

        return left;
    }

    private FilterNode ParseAnd()
    {
        var left = ParseBooleanTerm();
        while (PeekKeyword("and"))
        {
            Next();
            left = new AndNode(left, ParseBooleanTerm());
        }

        return left;
    }

    private FilterNode ParseBooleanTerm()
    {
        if (PeekKeyword("not"))
        {
            Next();
            return new NotNode(ParseNotOperand());
        }

        var left = ParseOperand();
        if (PeekComparisonOperator())
        {
            var op = Next();
            return BuildComparison(left, op, ParseOperand());
        }

        return ToBoolean(left);
    }

    // As in Azure, "not" binds tighter than comparisons, so "not Rating gt 5" is an error.
    private FilterNode ParseNotOperand()
    {
        if (PeekKeyword("not"))
        {
            Next();
            return new NotNode(ParseNotOperand());
        }

        var node = ToBoolean(ParseOperand());
        return PeekComparisonOperator()
            ? throw Error("'not' binds tighter than comparison operators; put the comparison in parentheses", Peek)
            : node;
    }

    private bool PeekComparisonOperator() => Peek.Kind == TokenKind.Identifier && ComparisonOperators.Contains(Peek.Text);

    private Operand ParseOperand()
    {
        var token = Peek;
        switch (token.Kind)
        {
            case TokenKind.OpenParen:
                Next();
                var inner = ParseOr();
                Expect(TokenKind.CloseParen, "')'");
                return new NodeOperand(inner);
            case TokenKind.String:
                Next();
                return new LiteralOperand(new Literal(LiteralKind.String, token.Text));
            case TokenKind.Number:
                Next();
                return new LiteralOperand(ParseNumber(token));
            case TokenKind.DateTimeOffset:
                Next();
                return new LiteralOperand(new Literal(LiteralKind.DateTimeOffset, ODataFilter.ToAzurePrecision(ParseDate(token))));
            case TokenKind.Identifier:
                switch (token.Text)
                {
                    case "true" or "false":
                        Next();
                        return new LiteralOperand(new Literal(LiteralKind.Boolean, token.Text == "true"));
                    case "null":
                        Next();
                        return new LiteralOperand(new Literal(LiteralKind.Null, null));
                    case "search.in":
                        Next();
                        return new NodeOperand(ParseSearchIn());
                    case "search.ismatch":
                        Next();
                        return new NodeOperand(ParseIsMatch());
                }

                if (token.Text.Contains('.'))
                {
                    throw Error($"the function '{token.Text}' isn't supported by the simulator", token);
                }

                if (Keywords.Contains(token.Text))
                {
                    throw Error($"unexpected '{token.Text}'", token);
                }

                return ParsePath();
            default:
                throw Error(token.Kind == TokenKind.End ? "unexpected end of filter" : $"unexpected '{token.Text}'", token);
        }
    }

    private Operand ParsePath()
    {
        var first = Next();
        if (_scopes.TryPeek(out var scope) && first.Text != scope)
        {
            // As in Azure, a lambda body can only use its own range variable, not document fields or outer variables.
            throw Error($"only fields bound to the range variable '{scope}' can be used inside its lambda, not '{first.Text}'", first);
        }

        FieldRef current;
        if (_variables.TryGetValue(first.Text, out var variable))
        {
            current = variable;
        }
        else
        {
            var field = _schema.Fields.FirstOrDefault(f => f.Name == first.Text)
                ?? throw Error($"could not find a field named '{first.Text}' in the index", first);
            current = new FieldRef(null, [field.Name], field.Name, field.Type, field, field.Fields);
        }

        while (Peek.Kind == TokenKind.Slash)
        {
            Next();
            var segment = Expect(TokenKind.Identifier, "a field name, 'any' or 'all'");
            if (segment.Text is "any" or "all" && Peek.Kind == TokenKind.OpenParen)
            {
                var lambda = ParseLambda(current, segment);
                if (Peek.Kind == TokenKind.Slash)
                {
                    throw Error("a lambda can't be navigated further", Peek);
                }

                return new NodeOperand(lambda);
            }

            if (current.IsCollection)
            {
                throw Error($"'{current.Path}' is a collection; use any() or all() to filter on its elements", segment);
            }

            if (current.Type != SearchFieldDataType.ComplexType)
            {
                throw Error($"'{current.Path}' isn't a complex field, so it has no sub-field '{segment.Text}'", segment);
            }

            var subField = current.SubFields?.FirstOrDefault(f => f.Name == segment.Text)
                ?? throw Error($"could not find a field named '{segment.Text}' under '{current.Path}'", segment);
            current = current with
            {
                Segments = [.. current.Segments, subField.Name],
                Path = $"{current.Path}/{subField.Name}",
                Type = subField.Type,
                Field = subField,
                SubFields = subField.Fields,
            };
        }

        return new FieldOperand(current);
    }

    private LambdaNode ParseLambda(FieldRef collection, Token kind)
    {
        if (!collection.IsCollection)
        {
            throw Error($"'{collection.Path}' isn't a collection, so it can't use {kind.Text}()", kind);
        }

        var all = kind.Text == "all";
        Expect(TokenKind.OpenParen, "'('");
        if (Peek.Kind == TokenKind.CloseParen)
        {
            Next();
            return all ? throw Error("all() needs a lambda expression", kind) : new LambdaNode(collection, false, null, null);
        }

        var name = Expect(TokenKind.Identifier, "a range variable");
        if (name.Text.Contains('.') || Keywords.Contains(name.Text) || _variables.ContainsKey(name.Text))
        {
            throw Error($"'{name.Text}' can't be used as a range variable here", name);
        }

        Expect(TokenKind.Colon, "':'");
        _variables[name.Text] = new FieldRef(name.Text, [], name.Text, collection.ElementType, collection.Field, collection.SubFields);
        _scopes.Push(name.Text);
        FilterNode body;
        try
        {
            body = ParseOr();
        }
        finally
        {
            _scopes.Pop();
            _variables.Remove(name.Text);
        }

        Expect(TokenKind.CloseParen, "')'");
        if (collection.ElementType != SearchFieldDataType.ComplexType)
        {
            ValidatePrimitiveLambda(collection, all, name.Text, body, kind);
        }

        return new LambdaNode(collection, all, name.Text, body);
    }

    // Azure's rules for lambdas over primitive collections; anything else is a 400 there.
    private void ValidatePrimitiveLambda(FieldRef collection, bool all, string variable, FilterNode body, Token at)
    {
        bool IsRangeVariable(FieldRef field) => field.Variable == variable && field.Segments.Length == 0;

        bool IsComparison(FilterNode node, params string[] operators) =>
            node is ComparisonNode c && IsRangeVariable(c.Field) && operators.Contains(c.Operator);

        // Strings: 'any' tests equality joined by 'or'; 'all' tests inequality joined by 'and'.
        bool ValidString(FilterNode node) => node switch
        {
            OrNode either when !all => ValidString(either.Left) && ValidString(either.Right),
            AndNode both when all => ValidString(both.Left) && ValidString(both.Right),
            ComparisonNode => IsComparison(node, all ? "ne" : "eq"),
            NotNode { Operand: var operand } when all => IsComparison(operand, "eq") || operand is SearchInNode s && IsRangeVariable(s.Field),
            SearchInNode s => !all && IsRangeVariable(s.Field),
            _ => false,
        };

        // Booleans: a single eq/ne test of the range variable, possibly negated.
        bool ValidBoolean(FilterNode node) => node switch
        {
            BooleanFieldNode b => IsRangeVariable(b.Field),
            NotNode n => ValidBoolean(n.Operand),
            _ => IsComparison(node, "eq", "ne"),
        };

        // Numbers and dates: 'any' takes ORs of ANDs, without 'ne' in an AND; 'all' takes ANDs of ORs, without 'eq' in an OR.
        bool ValidComparable()
        {
            var excluded = all ? "eq" : "ne";
            return Terms(body, or: !all).All(group =>
            {
                var terms = Terms(group, or: all).ToList();
                return terms.All(t => IsComparison(t, "eq", "ne", "lt", "le", "gt", "ge"))
                    && (terms.Count == 1 || !terms.Any(t => IsComparison(t, excluded)));
            });
        }

        var (valid, rule) = collection.ElementType switch
        {
            SearchFieldDataType.String => (ValidString(body), all
                ? "'ne', 'not (x eq y)' or 'not search.in' on the range variable, combined with 'and'"
                : "'eq' or search.in on the range variable, combined with 'or'"),
            SearchFieldDataType.Boolean => (ValidBoolean(body), "a single 'eq' or 'ne' test of the range variable"),
            _ => (ValidComparable(), all
                ? "comparisons of the range variable as ANDs of ORs, with no 'eq' in an OR"
                : "comparisons of the range variable as ORs of ANDs, with no 'ne' in an AND"),
        };

        if (!valid)
        {
            throw Error($"{at.Text}() over '{collection.Path}' ({collection.Type}) only allows {rule}", at);
        }
    }

    private static IEnumerable<FilterNode> Terms(FilterNode node, bool or) => node switch
    {
        OrNode either when or => Terms(either.Left, or).Concat(Terms(either.Right, or)),
        AndNode both when !or => Terms(both.Left, or).Concat(Terms(both.Right, or)),
        _ => [node],
    };

    private SearchInNode ParseSearchIn()
    {
        var open = Expect(TokenKind.OpenParen, "'('");
        if (ParseOperand() is not FieldOperand { Field: var field })
        {
            throw Error("search.in needs a field as its first argument", open);
        }

        if (field.Type != SearchFieldDataType.String)
        {
            throw Error($"search.in only applies to string fields, not '{field.Path}' ({field.Type})", open);
        }

        RequireFilterable(field, open);
        Expect(TokenKind.Comma, "','");
        var values = Expect(TokenKind.String, "a string of values").Text;
        var delimiters = " ,";
        if (Peek.Kind == TokenKind.Comma)
        {
            Next();
            delimiters = Expect(TokenKind.String, "a string of delimiters").Text;
            if (delimiters.Length == 0)
            {
                throw Error("search.in delimiters can't be empty", open);
            }
        }

        Expect(TokenKind.CloseParen, "')'");
        var set = values.Split(delimiters.ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
            .Select(v => Normalize(field, v))
            .ToHashSet(StringComparer.Ordinal);
        return new SearchInNode(field, set);
    }

    private IsMatchNode ParseIsMatch()
    {
        var open = Expect(TokenKind.OpenParen, "'('");
        if (_variables.Count > 0)
        {
            throw Error("search.ismatch can't be used inside a lambda expression", open);
        }

        var arguments = new List<string> { Expect(TokenKind.String, "a search string").Text };
        while (Peek.Kind == TokenKind.Comma && arguments.Count < 4)
        {
            Next();
            arguments.Add(Expect(TokenKind.String, "a string argument").Text);
        }

        Expect(TokenKind.CloseParen, "')'");
        var queryType = arguments.Count > 2 ? arguments[2] : "simple";
        var searchMode = arguments.Count > 3 ? arguments[3] : "any";
        if (queryType is not ("simple" or "full") || searchMode is not ("any" or "all"))
        {
            throw Error("search.ismatch's queryType must be 'simple' or 'full' and its searchMode 'any' or 'all'", open);
        }

        var searchFields = arguments.Count > 1 && arguments[1].Length > 0 ? arguments[1] : null;
        var node = new IsMatchNode(_isMatches.Count, arguments[0], searchFields, queryType, searchMode);
        _isMatches.Add(node);
        return node;
    }

    private ComparisonNode BuildComparison(Operand left, Token op, Operand right)
    {
        var (field, literal, @operator) = (left, right) switch
        {
            (FieldOperand f, LiteralOperand l) => (f.Field, l.Literal, op.Text),
            (LiteralOperand l, FieldOperand f) => (f.Field, l.Literal, Flip(op.Text)),
            (FieldOperand, FieldOperand) => throw Error("a comparison can't have a field on both sides", op),
            _ => throw Error("a comparison needs a field on one side and a constant on the other", op),
        };

        if (field.IsCollection || field.Type == SearchFieldDataType.ComplexType)
        {
            throw Error($"'{field.Path}' ({field.Type}) can't be compared directly", op);
        }

        RequireFilterable(field, op);
        var expected = field.Type switch
        {
            SearchFieldDataType.String => LiteralKind.String,
            SearchFieldDataType.Boolean => LiteralKind.Boolean,
            SearchFieldDataType.Int32 or SearchFieldDataType.Int64 or SearchFieldDataType.Double or "Edm.Single" => LiteralKind.Number,
            SearchFieldDataType.DateTimeOffset => LiteralKind.DateTimeOffset,
            _ => throw Error($"filtering on {field.Type} fields isn't supported by the simulator", op),
        };

        if (literal.Kind == LiteralKind.Null)
        {
            return @operator is "eq" or "ne" ? new ComparisonNode(field, @operator, literal) : throw Error($"'{@operator}' can't compare with null", op);
        }

        if (literal.Kind != expected)
        {
            throw Error($"'{field.Path}' is {field.Type} but is compared with a {literal.Kind} literal", op);
        }

        if (expected is LiteralKind.Boolean or LiteralKind.String && @operator is not ("eq" or "ne"))
        {
            throw Error($"'{@operator}' doesn't apply to {field.Type}; only 'eq' and 'ne' do", op);
        }

        return literal.Kind == LiteralKind.String
            ? new ComparisonNode(field, @operator, literal with { Value = Normalize(field, (string)literal.Value!) })
            : new ComparisonNode(field, @operator, literal);
    }

    private FilterNode ToBoolean(Operand operand) => operand switch
    {
        NodeOperand n => n.Node,
        LiteralOperand { Literal: { Kind: LiteralKind.Boolean, Value: bool value } } => new ConstantNode(value),
        FieldOperand { Field.Type: SearchFieldDataType.Boolean } f => new BooleanFieldNode(RequireFilterable(f.Field, Peek)),
        FieldOperand f => throw Error($"'{f.Field.Path}' ({f.Field.Type}) isn't a boolean expression", Peek),
        _ => throw Error("expected a boolean expression", Peek),
    };

    private FieldRef RequireFilterable(FieldRef field, Token at) =>
        field.Field?.Filterable == false ? throw Error($"'{field.Path}' isn't filterable", at) : field;

    private string Normalize(FieldRef field, string value) =>
        string.IsNullOrEmpty(field.Field?.Normalizer)
            ? value
            : NormalizerFactory.Normalize(value, field.Field.Normalizer, _schema.Normalizers, _schema.CharFilters);

    // .NET parses at most 7 fraction digits; Azure accepts more (e.g. Java's Instant.toString() nanoseconds).
    private DateTimeOffset ParseDate(Token token) =>
        DateTimeOffset.TryParse(Regex.Replace(token.Text, @"(\.\d{7})\d+", "$1"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : throw Error($"'{token.Text}' isn't a valid date", token);

    private static string Flip(string op) => op switch
    {
        "gt" => "lt",
        "lt" => "gt",
        "ge" => "le",
        "le" => "ge",
        _ => op,
    };

    private Literal ParseNumber(Token token) =>
        decimal.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact)
            ? new Literal(LiteralKind.Number, exact)
            : double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var approximate) && double.IsFinite(approximate)
                ? new Literal(LiteralKind.Number, approximate)
                : throw Error($"'{token.Text}' isn't a valid number", token);

    private ArgumentException Error(string message, Token at) =>
        new($"Invalid expression: {message} (at position {at.Position} in '{_filter}').\r\nParameter name: $filter");

    private static List<Token> Tokenize(string filter)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < filter.Length)
        {
            var c = filter[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var single = c switch
            {
                '(' => TokenKind.OpenParen,
                ')' => TokenKind.CloseParen,
                ',' => TokenKind.Comma,
                ':' => TokenKind.Colon,
                '/' => TokenKind.Slash,
                _ => (TokenKind?)null,
            };
            if (single is { } kind)
            {
                tokens.Add(new Token(kind, c.ToString(), i++));
                continue;
            }

            if (c == '\'')
            {
                var start = i++;
                var value = new System.Text.StringBuilder();
                while (true)
                {
                    if (i >= filter.Length)
                    {
                        throw new ArgumentException($"Invalid expression: unterminated string literal at position {start} in '{filter}'.\r\nParameter name: $filter");
                    }

                    if (filter[i] == '\'')
                    {
                        if (i + 1 < filter.Length && filter[i + 1] == '\'')
                        {
                            value.Append('\'');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    value.Append(filter[i++]);
                }

                tokens.Add(new Token(TokenKind.String, value.ToString(), start));
                continue;
            }

            Match match;
            if ((match = DateTimeLiteral.Match(filter, i)).Success)
            {
                tokens.Add(new Token(TokenKind.DateTimeOffset, match.Value, i));
            }
            else if ((match = NumberLiteral.Match(filter, i)).Success)
            {
                tokens.Add(new Token(TokenKind.Number, match.Value, i));
            }
            else if ((match = Identifier.Match(filter, i)).Success)
            {
                tokens.Add(new Token(TokenKind.Identifier, match.Value, i));
            }
            else
            {
                throw new ArgumentException($"Invalid expression: unexpected character '{c}' at position {i} in '{filter}'.\r\nParameter name: $filter");
            }

            i += match.Length;
        }

        tokens.Add(new Token(TokenKind.End, "end of filter", filter.Length));
        return tokens;
    }
}
