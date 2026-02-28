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
/// Tests that the Create Index response contains all fields and properties
/// that Azure AI Search returns, with proper defaults applied.
/// </summary>
public class IndexServiceDefaultsTests
{
    private readonly Mock<IIndexRepository> _repositoryMock;
    private readonly IndexService _sut;

    public IndexServiceDefaultsTests()
    {
        _repositoryMock = new Mock<IIndexRepository>();

        // Repository returns whatever is passed to CreateAsync (simulating storage)
        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<SearchIndex>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SearchIndex idx, CancellationToken _) =>
            {
                idx.ETag = "\"test-etag\"";
                return idx;
            });
        _repositoryMock
            .Setup(r => r.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchIndex>());

        var settings = Options.Create(new SimulatorSettings());
        var logger = new Mock<ILogger<IndexService>>();
        var luceneSettings = Options.Create(new LuceneSettings { IndexPath = Path.Combine(Path.GetTempPath(), "sim-test-" + Guid.NewGuid().ToString("N")) });
        var luceneManager = new LuceneIndexManager(new Mock<ILogger<LuceneIndexManager>>().Object, luceneSettings);

        _sut = new IndexService(_repositoryMock.Object, settings, luceneManager, logger.Object);
    }

    private static SearchIndex CreateMinimalIndex() => new()
    {
        Name = "test-index",
        Fields = new List<SearchField>
        {
            new() { Name = "id", Type = "Edm.String", Key = true, Filterable = true },
            new() { Name = "title", Type = "Edm.String", Searchable = true },
            new() { Name = "rating", Type = "Edm.Double", Filterable = true, Sortable = true },
            new() { Name = "active", Type = "Edm.Boolean", Filterable = true },
            new() { Name = "created", Type = "Edm.DateTimeOffset", Filterable = true, Sortable = true },
            new() { Name = "tags", Type = "Collection(Edm.String)", Searchable = true, Filterable = true }
        }
    };

    // ─── Field-level defaults ─────────────────────────────────────────

    [Fact]
    public async Task CreateIndex_StringKeyField_HasAllPropertiesSet()
    {
        var result = await _sut.CreateIndexAsync(CreateMinimalIndex());
        var field = result.Fields.Single(f => f.Name == "id");

        Assert.True(field.Key);
        Assert.True(field.Searchable);      // Edm.String defaults to searchable
        Assert.True(field.Filterable);       // explicitly set
        Assert.True(field.Retrievable);      // default true
        Assert.True(field.Stored);           // default true
        Assert.True(field.Sortable);         // Edm.String supports sortable
        Assert.True(field.Facetable);        // Edm.String supports facetable
        Assert.NotNull(field.SynonymMaps);
        Assert.Empty(field.SynonymMaps);
    }

    [Fact]
    public async Task CreateIndex_SearchableStringField_HasAllPropertiesSet()
    {
        var result = await _sut.CreateIndexAsync(CreateMinimalIndex());
        var field = result.Fields.Single(f => f.Name == "title");

        Assert.False(field.Key);
        Assert.True(field.Searchable);
        Assert.True(field.Filterable);       // default true for Edm.String
        Assert.True(field.Retrievable);
        Assert.True(field.Stored);
        Assert.True(field.Sortable);
        Assert.True(field.Facetable);
        Assert.NotNull(field.SynonymMaps);
        Assert.Empty(field.SynonymMaps);
    }

    [Fact]
    public async Task CreateIndex_DoubleField_SearchableIsFalse()
    {
        var result = await _sut.CreateIndexAsync(CreateMinimalIndex());
        var field = result.Fields.Single(f => f.Name == "rating");

        Assert.False(field.Key);
        Assert.False(field.Searchable);      // Edm.Double is not searchable
        Assert.True(field.Filterable);
        Assert.True(field.Retrievable);
        Assert.True(field.Stored);
        Assert.True(field.Sortable);
        Assert.True(field.Facetable);
    }

    [Fact]
    public async Task CreateIndex_BooleanField_SearchableIsFalse()
    {
        var result = await _sut.CreateIndexAsync(CreateMinimalIndex());
        var field = result.Fields.Single(f => f.Name == "active");

        Assert.False(field.Searchable);
        Assert.True(field.Filterable);
        Assert.True(field.Retrievable);
        Assert.True(field.Stored);
        Assert.True(field.Sortable);
        Assert.True(field.Facetable);
    }

    [Fact]
    public async Task CreateIndex_DateTimeOffsetField_SearchableIsFalse()
    {
        var result = await _sut.CreateIndexAsync(CreateMinimalIndex());
        var field = result.Fields.Single(f => f.Name == "created");

        Assert.False(field.Searchable);
        Assert.True(field.Filterable);
        Assert.True(field.Retrievable);
        Assert.True(field.Stored);
        Assert.True(field.Sortable);
        Assert.True(field.Facetable);
    }

    [Fact]
    public async Task CreateIndex_CollectionStringField_NotSortable()
    {
        var result = await _sut.CreateIndexAsync(CreateMinimalIndex());
        var field = result.Fields.Single(f => f.Name == "tags");

        Assert.True(field.Searchable);
        Assert.True(field.Filterable);
        Assert.True(field.Retrievable);
        Assert.True(field.Stored);
        Assert.False(field.Sortable);        // Collections cannot be sorted
        Assert.True(field.Facetable);
        Assert.NotNull(field.SynonymMaps);
    }

    [Fact]
    public async Task CreateIndex_AllFields_HaveNonNullBooleanAttributes()
    {
        var result = await _sut.CreateIndexAsync(CreateMinimalIndex());

        foreach (var field in result.Fields)
        {
            Assert.NotNull(field.Searchable);
            Assert.NotNull(field.Filterable);
            Assert.NotNull(field.Retrievable);
            Assert.NotNull(field.Stored);
            Assert.NotNull(field.Sortable);
            Assert.NotNull(field.Facetable);
            Assert.NotNull(field.SynonymMaps);
        }
    }

    // ─── Index-level defaults ─────────────────────────────────────────

    [Fact]
    public async Task CreateIndex_HasScoringProfilesEmptyList()
    {
        var result = await _sut.CreateIndexAsync(CreateMinimalIndex());
        Assert.NotNull(result.ScoringProfiles);
        Assert.Empty(result.ScoringProfiles);
    }

    [Fact]
    public async Task CreateIndex_HasSuggestersEmptyList()
    {
        var result = await _sut.CreateIndexAsync(CreateMinimalIndex());
        Assert.NotNull(result.Suggesters);
        Assert.Empty(result.Suggesters);
    }

    [Fact]
    public async Task CreateIndex_HasAnalyzersEmptyList()
    {
        var result = await _sut.CreateIndexAsync(CreateMinimalIndex());
        Assert.NotNull(result.Analyzers);
        Assert.Empty(result.Analyzers);
    }

    [Fact]
    public async Task CreateIndex_HasTokenizersEmptyList()
    {
        var result = await _sut.CreateIndexAsync(CreateMinimalIndex());
        Assert.NotNull(result.Tokenizers);
        Assert.Empty(result.Tokenizers);
    }

    [Fact]
    public async Task CreateIndex_HasTokenFiltersEmptyList()
    {
        var result = await _sut.CreateIndexAsync(CreateMinimalIndex());
        Assert.NotNull(result.TokenFilters);
        Assert.Empty(result.TokenFilters);
    }

    [Fact]
    public async Task CreateIndex_HasCharFiltersEmptyList()
    {
        var result = await _sut.CreateIndexAsync(CreateMinimalIndex());
        Assert.NotNull(result.CharFilters);
        Assert.Empty(result.CharFilters);
    }

    [Fact]
    public async Task CreateIndex_HasBM25Similarity()
    {
        var result = await _sut.CreateIndexAsync(CreateMinimalIndex());
        Assert.NotNull(result.Similarity);
        Assert.Equal("#Microsoft.Azure.Search.BM25Similarity", result.Similarity.ODataType);
        Assert.Null(result.Similarity.K1);
        Assert.Null(result.Similarity.B);
    }

    [Fact]
    public async Task CreateIndex_HasETag()
    {
        var result = await _sut.CreateIndexAsync(CreateMinimalIndex());
        Assert.NotNull(result.ETag);
        Assert.NotEmpty(result.ETag);
    }

    // ─── Explicit values are preserved ────────────────────────────────

    [Fact]
    public async Task CreateIndex_ExplicitFalseSearchable_IsPreserved()
    {
        var index = new SearchIndex
        {
            Name = "test-explicit",
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = false }
            }
        };

        var result = await _sut.CreateIndexAsync(index);
        var field = result.Fields.Single(f => f.Name == "title");

        Assert.False(field.Searchable); // Explicitly set to false, should not be overridden
    }

    [Fact]
    public async Task CreateIndex_WithExistingScoringProfiles_ArePreserved()
    {
        var index = CreateMinimalIndex();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new() { Name = "myProfile" }
        };

        var result = await _sut.CreateIndexAsync(index);
        Assert.Single(result.ScoringProfiles!);
        Assert.Equal("myProfile", result.ScoringProfiles![0].Name);
    }

    [Fact]
    public async Task CreateIndex_WithExistingSuggesters_ArePreserved()
    {
        var index = CreateMinimalIndex();
        // Add a searchable string field for the suggester
        index.Suggesters = new List<Suggester>
        {
            new() { Name = "sg", SourceFields = new List<string> { "title" } }
        };

        var result = await _sut.CreateIndexAsync(index);
        Assert.Single(result.Suggesters!);
        Assert.Equal("sg", result.Suggesters![0].Name);
    }
}
