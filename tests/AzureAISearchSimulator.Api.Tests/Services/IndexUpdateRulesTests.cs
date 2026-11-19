using AzureAISearchSimulator.Api.Services;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Search;
using AzureAISearchSimulator.Storage.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AzureAISearchSimulator.Api.Tests.Services;

/// <summary>
/// Tests for the rules Azure applies to updates of an existing index: fields can be added but not deleted or changed
/// (except retrievable, searchAnalyzer and synonymMaps), suggesters only gain new fields, analysis components can't
/// change and are only added with downtime, and vector algorithms in use can't change.
/// </summary>
public class IndexUpdateRulesTests
{
    private readonly IndexService _sut;

    public IndexUpdateRulesTests()
    {
        var repositoryMock = new Mock<IIndexRepository>();
        repositoryMock
            .Setup(r => r.GetByNameAsync("test-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateIndex());
        repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<SearchIndex>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SearchIndex idx, CancellationToken _) => idx);

        var luceneSettings = Options.Create(new LuceneSettings { IndexPath = Path.Combine(Path.GetTempPath(), "sim-test-" + Guid.NewGuid().ToString("N")) });
        var luceneManager = new LuceneIndexManager(new Mock<ILogger<LuceneIndexManager>>().Object, luceneSettings);

        _sut = new IndexService(repositoryMock.Object, Options.Create(new SimulatorSettings()), luceneManager, new Mock<ILogger<IndexService>>().Object);
    }

    public static TheoryData<string, Action<SearchIndex>, bool> AllowedUpdates => new()
    {
        { "unchanged", _ => { }, false },
        { "field added", i => i.Fields.Add(new SearchField { Name = "added", Type = "Edm.String" }), false },
        { "sub-field added", i => Field(i, "address").Fields!.Add(new SearchField { Name = "zip", Type = "Edm.String" }), false },
        { "retrievable changed", i => Field(i, "title").Retrievable = false, false },
        { "synonym maps changed", i => Field(i, "title").SynonymMaps = ["synonyms"], false },
        { "new field added to a suggester", i => AddFieldToSuggester(i), false },
        { "analyzer added with downtime", i => AddAnalyzer(i), true },
        { "unused vector algorithm changed", i => i.VectorSearch!.Algorithms![1].HnswParameters!.Metric = "dotProduct", false },
    };

    public static TheoryData<string, Action<SearchIndex>, string> RefusedUpdates => new()
    {
        { "field deleted", i => { i.Fields.RemoveAll(f => f.Name == "address"); }, "'address', 'address/city' cannot be deleted" },
        { "sub-field deleted", i => Field(i, "address").Fields!.Clear(), "'address/city' cannot be deleted" },
        { "filterable changed", i => Field(i, "title").Filterable = false, "'title' cannot be changed" },
        { "sub-field type changed", i => Field(i, "address").Fields![0].Type = "Edm.Int32", "'address/city' cannot be changed" },
        { "analyzer changed", i => Field(i, "title").Analyzer = "en.lucene", "'title' cannot be changed" },
        { "new suggester over an existing field", i => { i.Suggesters!.Clear(); i.Suggesters.Add(new Suggester { Name = "sg2", SourceFields = ["title"] }); }, "cannot be referenced by a new suggester" },
        { "field removed from a suggester", i => i.Suggesters![0].SourceFields.Remove("title"), "Cannot remove field(s) 'title'" },
        { "existing field added to a suggester", i => i.Suggesters![0].SourceFields.Add("address/city"), "cannot be added to suggester 'sg'" },
        { "suggester removed", i => i.Suggesters!.Clear(), "suggesters defined in it that cannot be removed" },
        { "custom analyzer changed", i => i.Analyzers![0].TokenFilters = ["lowercase", "asciifolding"], "Existing analyzers configuration cannot be modified" },
        { "custom analyzer removed", i => i.Analyzers!.Clear(), "Existing analyzers configuration cannot be modified" },
        { "analyzer added without downtime", i => AddAnalyzer(i), "allowIndexDowntime" },
        { "vector algorithm in use changed", i => i.VectorSearch!.Algorithms![0].HnswParameters!.Metric = "euclidean", "already used by an existing vector field" },
        { "vector profile pointed to another algorithm", i => i.VectorSearch!.Profiles![0].Algorithm = "hnsw2", "Algorithm name cannot be updated" },
    };

    [Theory]
    [MemberData(nameof(AllowedUpdates))]
    public async Task CreateOrUpdateIndex_AllowedUpdate_Succeeds(string update, Action<SearchIndex> change, bool allowIndexDowntime)
    {
        // Arrange
        var index = CreateIndex();
        change(index);

        // Act
        var result = await _sut.CreateOrUpdateIndexAsync("test-index", index, allowIndexDowntime);

        // Assert
        Assert.True(result is not null, update);
    }

    [Theory]
    [MemberData(nameof(RefusedUpdates))]
    public async Task CreateOrUpdateIndex_UpdateAzureCannotApply_Throws(string update, Action<SearchIndex> change, string expectedMessage)
    {
        // Arrange
        var index = CreateIndex();
        change(index);

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateOrUpdateIndexAsync("test-index", index));

        // Assert
        Assert.True(ex.Message.Contains(expectedMessage, StringComparison.Ordinal), $"{update}: {ex.Message}");
    }

    private static SearchField Field(SearchIndex index, string name) => index.Fields.Single(f => f.Name == name);

    private static void AddFieldToSuggester(SearchIndex index)
    {
        index.Fields.Add(new SearchField { Name = "added", Type = "Edm.String", Searchable = true });
        index.Suggesters![0].SourceFields.Add("added");
    }

    private static void AddAnalyzer(SearchIndex index) =>
        index.Analyzers!.Add(new CustomAnalyzer { Name = "other", Tokenizer = "standard_v2", TokenFilters = ["lowercase"] });

    private static SearchIndex CreateIndex() => new()
    {
        Name = "test-index",
        Fields =
        [
            new SearchField { Name = "id", Type = "Edm.String", Key = true },
            new SearchField { Name = "title", Type = "Edm.String", Searchable = true },
            new SearchField { Name = "tags", Type = "Collection(Edm.String)", Filterable = true },
            new SearchField
            {
                Name = "address",
                Type = "Edm.ComplexType",
                Fields = [new SearchField { Name = "city", Type = "Edm.String" }],
            },
            new SearchField { Name = "embedding", Type = "Collection(Edm.Single)", Searchable = true, Dimensions = 2, VectorSearchProfile = "p" },
        ],
        Suggesters = [new Suggester { Name = "sg", SourceFields = ["title", "tags"] }],
        Analyzers = [new CustomAnalyzer { Name = "my", Tokenizer = "standard_v2", TokenFilters = ["lowercase"] }],
        VectorSearch = new VectorSearchConfiguration
        {
            Algorithms =
            [
                new VectorSearchAlgorithm { Name = "hnsw1", Kind = "hnsw", HnswParameters = new HnswParameters { Metric = "cosine" } },
                new VectorSearchAlgorithm { Name = "hnsw2", Kind = "hnsw", HnswParameters = new HnswParameters { Metric = "cosine" } },
            ],
            Profiles = [new VectorSearchProfile { Name = "p", Algorithm = "hnsw1" }],
        },
    };
}
