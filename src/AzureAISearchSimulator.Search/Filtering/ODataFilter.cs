using System.Globalization;
using System.Text.Json;
using AzureAISearchSimulator.Core.Models;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;

namespace AzureAISearchSimulator.Search.Filtering;

/// <summary>
/// Evaluates a parsed $filter exactly, per document, against the stored source JSON. Lambdas bind
/// each collection element in turn, so conditions inside one any()/all() hold for the same element
/// (flattened Lucene fields can't tell which element a value came from). Simple top-level equality
/// clauses narrow the candidates through the Lucene index first.
/// </summary>
internal static class ODataFilter
{
    private const string SourceField = "_raw_json";

    /// <summary>
    /// Truncates a date to the millisecond in UTC, the precision at which Azure keeps <c>Edm.DateTimeOffset</c> values
    /// and filter literals.
    /// </summary>
    /// <param name="value">The date.</param>
    /// <returns>The date in UTC, to the millisecond.</returns>
    internal static DateTimeOffset ToAzurePrecision(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);

    /// <summary>
    /// Parses a <c>$filter</c> and returns a Lucene query that matches exactly the documents it selects, without
    /// changing their scores.
    /// </summary>
    /// <param name="schema">The index definition.</param>
    /// <param name="filter">The <c>$filter</c> expression.</param>
    /// <param name="buildTextQuery">Builds the text query for a <c>search.ismatch</c> call.</param>
    /// <returns>A constant-score query for the filter.</returns>
    /// <exception cref="ArgumentException">The filter is invalid or can't be evaluated exactly.</exception>
    public static Query ToQuery(SearchIndex schema, string filter, Func<IsMatchNode, Query> buildTextQuery)
    {
        var parsed = ODataFilterParser.Parse(schema, filter);
        var isMatchQueries = parsed.IsMatches.Select(buildTextQuery).ToArray();
        return new ConstantScoreQuery(new SourceFilter(schema, filter, parsed.Root, isMatchQueries, BuildCandidateQuery(parsed.Root)));
    }

    /// <summary>
    /// Exact Lucene terms for top-level string equality, so only documents that can match are evaluated. A clause
    /// that would exceed Lucene's clause limit is left to the exact evaluation instead.
    /// </summary>
    private static Query? BuildCandidateQuery(FilterNode root)
    {
        var candidates = new BooleanQuery();
        foreach (var clause in Conjuncts(root))
        {
            if (candidates.Clauses.Count >= BooleanQuery.MaxClauseCount)
            {
                break;
            }

            switch (clause)
            {
                case ComparisonNode { Operator: "eq", Value: { Kind: LiteralKind.String, Value: string value }, Field: var field } when IndexedExactly(field):
                    candidates.Add(new TermQuery(new Term(LuceneDocumentMapper.GetFilterFieldName(field.Field!), value)), Occur.MUST);
                    break;
                case SearchInNode { Field: var field, Values: var values } when IndexedExactly(field) && values.Count <= BooleanQuery.MaxClauseCount:
                    var any = new BooleanQuery();
                    foreach (var value in values)
                    {
                        any.Add(new TermQuery(new Term(LuceneDocumentMapper.GetFilterFieldName(field.Field!), value)), Occur.SHOULD);
                    }

                    candidates.Add(any, Occur.MUST);
                    break;
            }
        }

        return candidates.Clauses.Count > 0 ? candidates : null;
    }

    /// <summary>
    /// Whether a field's raw string values are indexed as single terms, so equality can narrow through the index.
    /// </summary>
    private static bool IndexedExactly(FieldRef field) =>
        field is { Variable: null, Segments.Length: 1, Type: SearchFieldDataType.String, Field: { } f }
        && string.IsNullOrEmpty(f.Normalizer) && (f.Key || f.Filterable == true);

    /// <summary>
    /// The clauses joined by top-level <c>and</c>s.
    /// </summary>
    private static IEnumerable<FilterNode> Conjuncts(FilterNode node) =>
        node is AndNode both ? Conjuncts(both.Left).Concat(Conjuncts(both.Right)) : [node];

    /// <summary>
    /// A Lucene filter that evaluates the parsed filter against each candidate document's source.
    /// </summary>
    private sealed class SourceFilter(SearchIndex schema, string text, FilterNode root, Query[] isMatchQueries, Query? candidateQuery) : Lucene.Net.Search.Filter
    {
        private static readonly ISet<string> SourceOnly = new HashSet<string> { SourceField };

        /// <inheritdoc />
        public override DocIdSet GetDocIdSet(AtomicReaderContext context, IBits acceptDocs)
        {
            var reader = context.AtomicReader;
            DocIdSetIterator? candidates = null;
            if (candidateQuery is not null && (candidates = new QueryWrapperFilter(candidateQuery).GetDocIdSet(context, acceptDocs)?.GetIterator()) is null)
            {
                return null!;
            }

            var isMatches = isMatchQueries.Select(q => Collect(new QueryWrapperFilter(q).GetDocIdSet(context, acceptDocs), reader.MaxDoc)).ToArray();
            var matches = new FixedBitSet(reader.MaxDoc);
            var doc = -1;
            while ((doc = candidates?.NextDoc() ?? (doc + 1 < reader.MaxDoc ? doc + 1 : DocIdSetIterator.NO_MORE_DOCS)) != DocIdSetIterator.NO_MORE_DOCS)
            {
                if (acceptDocs is not null && !acceptDocs.Get(doc))
                {
                    continue;
                }

                var json = reader.Document(doc, SourceOnly).Get(SourceField);
                if (json is null)
                {
                    continue;
                }

                using var source = JsonDocument.Parse(json);
                if (Evaluate(root, source.RootElement, null, new Context(schema, isMatches, doc)))
                {
                    matches.Set(doc);
                }
            }

            return matches;
        }

        /// <inheritdoc />
        public override string ToString() => $"ODataFilter({text})";

        /// <summary>
        /// The documents in a set, as bits.
        /// </summary>
        private static FixedBitSet Collect(DocIdSet? set, int maxDoc)
        {
            var bits = new FixedBitSet(maxDoc);
            if (set?.GetIterator() is { } iterator)
            {
                bits.Or(iterator);
            }

            return bits;
        }
    }

    /// <summary>
    /// What evaluating one document needs besides its source: the schema, the <c>search.ismatch</c> results and the document.
    /// </summary>
    private readonly record struct Context(SearchIndex Schema, FixedBitSet[] IsMatches, int Doc);

    /// <summary>
    /// Range variables bound by enclosing lambdas, innermost first.
    /// </summary>
    private sealed record Binding(string Name, JsonElement Value, Binding? Outer);

    /// <summary>
    /// Whether a document satisfies a filter node.
    /// </summary>
    private static bool Evaluate(FilterNode node, JsonElement document, Binding? bindings, Context context) => node switch
    {
        AndNode both => Evaluate(both.Left, document, bindings, context) && Evaluate(both.Right, document, bindings, context),
        OrNode either => Evaluate(either.Left, document, bindings, context) || Evaluate(either.Right, document, bindings, context),
        NotNode negation => !Evaluate(negation.Operand, document, bindings, context),
        ConstantNode constant => constant.Value,
        BooleanFieldNode field => Resolve(field.Field, document, bindings) is { ValueKind: JsonValueKind.True },
        ComparisonNode comparison => Compare(comparison, Resolve(comparison.Field, document, bindings), context.Schema),
        SearchInNode searchIn => Resolve(searchIn.Field, document, bindings) is { ValueKind: JsonValueKind.String } value
            && searchIn.Values.Contains(Normalize(searchIn.Field, value.GetString()!, context.Schema)),
        IsMatchNode isMatch => context.IsMatches[isMatch.Index].Get(context.Doc),
        LambdaNode lambda => EvaluateLambda(lambda, document, bindings, context),
        _ => throw new InvalidOperationException($"Unhandled filter node {node.GetType().Name}"),
    };

    /// <summary>
    /// Evaluates <c>any</c> or <c>all</c> by binding each element of the collection in turn.
    /// </summary>
    private static bool EvaluateLambda(LambdaNode lambda, JsonElement document, Binding? bindings, Context context)
    {
        if (Resolve(lambda.Collection, document, bindings) is not { ValueKind: JsonValueKind.Array } collection)
        {
            return lambda.All;
        }

        if (lambda.Body is null)
        {
            return collection.GetArrayLength() > 0;
        }

        foreach (var element in collection.EnumerateArray())
        {
            var holds = Evaluate(lambda.Body, document, new Binding(lambda.Variable!, element, bindings), context);
            if (holds != lambda.All)
            {
                return holds;
            }
        }

        return lambda.All;
    }

    /// <summary>
    /// The value at a field path, from the document or a range variable; missing and null values both resolve to
    /// null, as in Azure.
    /// </summary>
    private static JsonElement? Resolve(FieldRef field, JsonElement document, Binding? bindings)
    {
        var current = document;
        if (field.Variable is not null)
        {
            var binding = bindings;
            while (binding!.Name != field.Variable)
            {
                binding = binding.Outer;
            }

            current = binding.Value;
        }

        foreach (var segment in field.Segments)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : current;
    }

    /// <summary>
    /// Whether a value satisfies a comparison; a missing value only satisfies <c>ne</c> and <c>eq null</c>.
    /// </summary>
    private static bool Compare(ComparisonNode comparison, JsonElement? value, SearchIndex schema)
    {
        if (comparison.Value.Kind == LiteralKind.Null)
        {
            return (value is null) == (comparison.Operator == "eq");
        }

        if (value is null || CompareTo(comparison, value.Value, schema) is not { } order)
        {
            return comparison.Operator == "ne";
        }

        return comparison.Operator switch
        {
            "eq" => order == 0,
            "ne" => order != 0,
            "lt" => order < 0,
            "le" => order <= 0,
            "gt" => order > 0,
            "ge" => order >= 0,
            _ => throw new InvalidOperationException($"Unhandled operator {comparison.Operator}"),
        };
    }

    /// <summary>
    /// The document value's order relative to the literal, or null when it isn't of the field's type or is <c>NaN</c>.
    /// </summary>
    private static int? CompareTo(ComparisonNode comparison, JsonElement value, SearchIndex schema)
    {
        var literal = comparison.Value.Value;
        switch (comparison.Value.Kind)
        {
            case LiteralKind.String when value.ValueKind == JsonValueKind.String:
                return Math.Sign(string.CompareOrdinal(Normalize(comparison.Field, value.GetString()!, schema), (string)literal!));
            case LiteralKind.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                return value.GetBoolean() == (bool)literal! ? 0 : 1;
            case LiteralKind.Number when value.ValueKind == JsonValueKind.Number:
                // Integer fields compare exactly; for Edm.Double, Azure converts the literal to a double first.
                var number = (NumberLiteral)literal!;
                return comparison.Field.ElementType is SearchFieldDataType.Int32 or SearchFieldDataType.Int64
                    && number.Exact is { } exact && value.TryGetDecimal(out var stored)
                    ? stored.CompareTo(exact)
                    : value.GetDouble().CompareTo(number.Approximate);
            case LiteralKind.Number when value.ValueKind == JsonValueKind.String && value.GetString() is "INF" or "-INF" or "NaN":
                // Azure's named doubles; NaN has no order, so only ne matches it.
                var named = value.GetString() switch { "INF" => double.PositiveInfinity, "-INF" => double.NegativeInfinity, _ => double.NaN };
                return double.IsNaN(named) ? null : named.CompareTo(((NumberLiteral)literal!).Approximate);
            case LiteralKind.DateTimeOffset when value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date):
                return ToAzurePrecision(date).UtcTicks.CompareTo(((DateTimeOffset)literal!).UtcTicks);
            default:
                return null;
        }
    }

    /// <summary>
    /// Applies the field's normalizer, if it has one, as Azure does to both sides of a comparison.
    /// </summary>
    private static string Normalize(FieldRef field, string value, SearchIndex schema) =>
        string.IsNullOrEmpty(field.Field?.Normalizer)
            ? value
            : NormalizerFactory.Normalize(value, field.Field.Normalizer, schema.Normalizers, schema.CharFilters);
}
