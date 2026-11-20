using Lucene.Net.Index;
using Lucene.Net.Queries;
using Lucene.Net.Search;

namespace AzureAISearchSimulator.Search;

/// <summary>
/// Multiplies each document's score by its scoring-profile function boost inside Lucene's scoring, so that
/// selecting the top hits, sorting by <c>search.score()</c> and breaking sort ties all use the boosted score.
/// </summary>
internal sealed class ScoringProfileBoostQuery : CustomScoreQuery
{
    private readonly Func<Lucene.Net.Documents.Document, double> _boost;

    /// <summary>
    /// Wraps a query so each match's score is multiplied by the boost computed from its stored document.
    /// </summary>
    /// <param name="subQuery">The query whose matches and scores are boosted.</param>
    /// <param name="boost">Computes a document's boost from its stored fields.</param>
    public ScoringProfileBoostQuery(Query subQuery, Func<Lucene.Net.Documents.Document, double> boost)
        : base(subQuery)
    {
        _boost = boost;
    }

    /// <inheritdoc />
    protected override CustomScoreProvider GetCustomScoreProvider(AtomicReaderContext context) =>
        new BoostProvider(context, _boost);

    private sealed class BoostProvider(AtomicReaderContext context, Func<Lucene.Net.Documents.Document, double> boost)
        : CustomScoreProvider(context)
    {
        public override float CustomScore(int doc, float subQueryScore, float valSrcScore) =>
            subQueryScore * (float)boost(m_context.AtomicReader.Document(doc));
    }
}
