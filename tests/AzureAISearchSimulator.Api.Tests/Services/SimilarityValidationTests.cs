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
/// Tests for similarity algorithm validation (Phase 3):
/// - BM25 parameter range validation
/// - ClassicSimilarity with k1/b rejection
/// - Unrecognized ODataType rejection
/// - Immutability enforcement on update
/// - allowIndexDowntime parameter support
/// </summary>
public class SimilarityValidationTests
{
    private readonly Mock<IIndexRepository> _repositoryMock;
    private readonly IndexService _sut;

    public SimilarityValidationTests()
    {
        _repositoryMock = new Mock<IIndexRepository>();

        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<SearchIndex>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SearchIndex idx, CancellationToken _) =>
            {
                idx.ETag = "\"test-etag\"";
                return idx;
            });
        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<SearchIndex>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SearchIndex idx, CancellationToken _) =>
            {
                idx.ETag = "\"test-etag-updated\"";
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

    private static SearchIndex CreateMinimalIndex(string name = "test-index") => new()
    {
        Name = name,
        Fields = new List<SearchField>
        {
            new() { Name = "id", Type = "Edm.String", Key = true },
            new() { Name = "content", Type = "Edm.String", Searchable = true }
        }
    };

    // ─── BM25 Parameter Validation ────────────────────────────────────

    [Fact]
    public async Task CreateIndex_BM25WithValidK1_Succeeds()
    {
        var index = CreateMinimalIndex();
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 1.5,
            B = 0.75
        };

        var result = await _sut.CreateIndexAsync(index);

        Assert.Equal(1.5, result.Similarity!.K1);
        Assert.Equal(0.75, result.Similarity.B);
    }

    [Fact]
    public async Task CreateIndex_BM25WithZeroK1_Succeeds()
    {
        var index = CreateMinimalIndex();
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 0.0
        };

        var result = await _sut.CreateIndexAsync(index);
        Assert.Equal(0.0, result.Similarity!.K1);
    }

    [Fact]
    public async Task CreateIndex_BM25WithNegativeK1_ThrowsValidationError()
    {
        var index = CreateMinimalIndex();
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = -0.5
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateIndexAsync(index));
        Assert.Contains("k1", ex.Message);
        Assert.Contains("non-negative", ex.Message);
    }

    [Fact]
    public async Task CreateIndex_BM25WithBAtZero_Succeeds()
    {
        var index = CreateMinimalIndex();
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            B = 0.0
        };

        var result = await _sut.CreateIndexAsync(index);
        Assert.Equal(0.0, result.Similarity!.B);
    }

    [Fact]
    public async Task CreateIndex_BM25WithBAtOne_Succeeds()
    {
        var index = CreateMinimalIndex();
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            B = 1.0
        };

        var result = await _sut.CreateIndexAsync(index);
        Assert.Equal(1.0, result.Similarity!.B);
    }

    [Fact]
    public async Task CreateIndex_BM25WithBAboveOne_ThrowsValidationError()
    {
        var index = CreateMinimalIndex();
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            B = 1.5
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateIndexAsync(index));
        Assert.Contains("b", ex.Message);
        Assert.Contains("0.0 and 1.0", ex.Message);
    }

    [Fact]
    public async Task CreateIndex_BM25WithNegativeB_ThrowsValidationError()
    {
        var index = CreateMinimalIndex();
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            B = -0.1
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateIndexAsync(index));
        Assert.Contains("b", ex.Message);
        Assert.Contains("0.0 and 1.0", ex.Message);
    }

    [Fact]
    public async Task CreateIndex_BM25WithNullParams_Succeeds()
    {
        var index = CreateMinimalIndex();
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity"
            // K1 and B are null — defaults applied by Lucene
        };

        var result = await _sut.CreateIndexAsync(index);
        Assert.Null(result.Similarity!.K1);
        Assert.Null(result.Similarity.B);
    }

    // ─── ClassicSimilarity Validation ─────────────────────────────────

    [Fact]
    public async Task CreateIndex_ClassicSimilarity_Succeeds()
    {
        var index = CreateMinimalIndex();
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.ClassicSimilarity"
        };

        var result = await _sut.CreateIndexAsync(index);
        Assert.Equal("#Microsoft.Azure.Search.ClassicSimilarity", result.Similarity!.ODataType);
    }

    [Fact]
    public async Task CreateIndex_ClassicSimilarityWithK1_ThrowsValidationError()
    {
        var index = CreateMinimalIndex();
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.ClassicSimilarity",
            K1 = 1.2
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateIndexAsync(index));
        Assert.Contains("ClassicSimilarity", ex.Message);
        Assert.Contains("k1", ex.Message);
    }

    [Fact]
    public async Task CreateIndex_ClassicSimilarityWithB_ThrowsValidationError()
    {
        var index = CreateMinimalIndex();
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.ClassicSimilarity",
            B = 0.75
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateIndexAsync(index));
        Assert.Contains("ClassicSimilarity", ex.Message);
        Assert.Contains("b", ex.Message);
    }

    [Fact]
    public async Task CreateIndex_ClassicSimilarityWithBothParams_ThrowsWithBothErrors()
    {
        var index = CreateMinimalIndex();
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.ClassicSimilarity",
            K1 = 1.2,
            B = 0.75
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateIndexAsync(index));
        Assert.Contains("k1", ex.Message);
        Assert.Contains("b", ex.Message);
    }

    // ─── Unknown ODataType ────────────────────────────────────────────

    [Fact]
    public async Task CreateIndex_UnknownSimilarityType_ThrowsValidationError()
    {
        var index = CreateMinimalIndex();
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.UnknownSimilarity"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateIndexAsync(index));
        Assert.Contains("Unknown similarity algorithm type", ex.Message);
    }

    [Fact]
    public async Task CreateIndex_EmptyODataType_ThrowsValidationError()
    {
        var index = CreateMinimalIndex();
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = ""
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateIndexAsync(index));
        Assert.Contains("@odata.type", ex.Message);
    }

    // ─── Immutability on Update ───────────────────────────────────────

    [Fact]
    public async Task UpdateIndex_ChangeSimilarityType_ThrowsError()
    {
        var existingIndex = CreateMinimalIndex();
        existingIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity"
        };

        _repositoryMock
            .Setup(r => r.GetByNameAsync("test-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIndex);

        var updatedIndex = CreateMinimalIndex();
        updatedIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.ClassicSimilarity"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateOrUpdateIndexAsync("test-index", updatedIndex));
        Assert.Contains("cannot be modified", ex.Message);
    }

    [Fact]
    public async Task UpdateIndex_ChangeFromClassicToBM25_ThrowsError()
    {
        var existingIndex = CreateMinimalIndex();
        existingIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.ClassicSimilarity"
        };

        _repositoryMock
            .Setup(r => r.GetByNameAsync("test-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIndex);

        var updatedIndex = CreateMinimalIndex();
        updatedIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateOrUpdateIndexAsync("test-index", updatedIndex));
        Assert.Contains("cannot be modified", ex.Message);
    }

    [Fact]
    public async Task UpdateIndex_SameSimilarityType_Succeeds()
    {
        var existingIndex = CreateMinimalIndex();
        existingIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 1.2,
            B = 0.75
        };

        _repositoryMock
            .Setup(r => r.GetByNameAsync("test-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIndex);

        var updatedIndex = CreateMinimalIndex();
        updatedIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 1.2,
            B = 0.75
        };

        var result = await _sut.CreateOrUpdateIndexAsync("test-index", updatedIndex);
        Assert.Equal("#Microsoft.Azure.Search.BM25Similarity", result.Similarity!.ODataType);
    }

    // ─── allowIndexDowntime ───────────────────────────────────────────

    [Fact]
    public async Task UpdateIndex_ChangeBM25Params_WithoutAllowDowntime_ThrowsError()
    {
        var existingIndex = CreateMinimalIndex();
        existingIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 1.2,
            B = 0.75
        };

        _repositoryMock
            .Setup(r => r.GetByNameAsync("test-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIndex);

        var updatedIndex = CreateMinimalIndex();
        updatedIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 2.0,
            B = 0.5
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateOrUpdateIndexAsync("test-index", updatedIndex, allowIndexDowntime: false));
        Assert.Contains("allowIndexDowntime", ex.Message);
    }

    [Fact]
    public async Task UpdateIndex_ChangeBM25Params_WithAllowDowntime_Succeeds()
    {
        var existingIndex = CreateMinimalIndex();
        existingIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 1.2,
            B = 0.75
        };

        _repositoryMock
            .Setup(r => r.GetByNameAsync("test-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIndex);

        var updatedIndex = CreateMinimalIndex();
        updatedIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 2.0,
            B = 0.5
        };

        var result = await _sut.CreateOrUpdateIndexAsync("test-index", updatedIndex, allowIndexDowntime: true);
        Assert.Equal(2.0, result.Similarity!.K1);
        Assert.Equal(0.5, result.Similarity.B);
    }

    [Fact]
    public async Task UpdateIndex_ChangeOnlyK1_WithoutAllowDowntime_ThrowsError()
    {
        var existingIndex = CreateMinimalIndex();
        existingIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 1.2,
            B = 0.75
        };

        _repositoryMock
            .Setup(r => r.GetByNameAsync("test-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIndex);

        var updatedIndex = CreateMinimalIndex();
        updatedIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 2.0,
            B = 0.75
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateOrUpdateIndexAsync("test-index", updatedIndex));
        Assert.Contains("allowIndexDowntime", ex.Message);
    }

    [Fact]
    public async Task UpdateIndex_ChangeOnlyB_WithAllowDowntime_Succeeds()
    {
        var existingIndex = CreateMinimalIndex();
        existingIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 1.2,
            B = 0.75
        };

        _repositoryMock
            .Setup(r => r.GetByNameAsync("test-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIndex);

        var updatedIndex = CreateMinimalIndex();
        updatedIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 1.2,
            B = 0.5
        };

        var result = await _sut.CreateOrUpdateIndexAsync("test-index", updatedIndex, allowIndexDowntime: true);
        Assert.Equal(0.5, result.Similarity!.B);
    }

    [Fact]
    public async Task UpdateIndex_ClassicToClassic_AlwaysSucceeds()
    {
        var existingIndex = CreateMinimalIndex();
        existingIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.ClassicSimilarity"
        };

        _repositoryMock
            .Setup(r => r.GetByNameAsync("test-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIndex);

        var updatedIndex = CreateMinimalIndex();
        updatedIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.ClassicSimilarity"
        };

        var result = await _sut.CreateOrUpdateIndexAsync("test-index", updatedIndex);
        Assert.Equal("#Microsoft.Azure.Search.ClassicSimilarity", result.Similarity!.ODataType);
    }

    // ─── Edge Cases ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateIndex_NullSimilarity_DefaultsToBM25_MatchesExisting()
    {
        var existingIndex = CreateMinimalIndex();
        existingIndex.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity"
        };

        _repositoryMock
            .Setup(r => r.GetByNameAsync("test-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIndex);

        // Submit update with null similarity — should default to BM25 and match
        var updatedIndex = CreateMinimalIndex();
        updatedIndex.Similarity = null;

        var result = await _sut.CreateOrUpdateIndexAsync("test-index", updatedIndex);
        Assert.Equal("#Microsoft.Azure.Search.BM25Similarity", result.Similarity!.ODataType);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.2)]
    [InlineData(3.0)]
    [InlineData(100.0)]
    public async Task CreateIndex_BM25WithValidK1Range_Succeeds(double k1)
    {
        var index = CreateMinimalIndex($"test-k{k1.ToString(System.Globalization.CultureInfo.InvariantCulture).Replace('.', '-')}");
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = k1
        };

        var result = await _sut.CreateIndexAsync(index);
        Assert.Equal(k1, result.Similarity!.K1);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public async Task CreateIndex_BM25WithValidBRange_Succeeds(double b)
    {
        var index = CreateMinimalIndex($"test-b{b.ToString(System.Globalization.CultureInfo.InvariantCulture).Replace('.', '-')}");
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            B = b
        };

        var result = await _sut.CreateIndexAsync(index);
        Assert.Equal(b, result.Similarity!.B);
    }

    [Fact]
    public async Task ValidateIndex_BM25WithValidParams_ReturnsSuccess()
    {
        var index = CreateMinimalIndex();
        index.Similarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 1.2,
            B = 0.75
        };

        var result = _sut.ValidateIndex(index);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateIndex_NullSimilarity_ReturnsSuccess()
    {
        var index = CreateMinimalIndex();
        index.Similarity = null;

        var result = _sut.ValidateIndex(index);
        Assert.True(result.IsValid);
    }
}
