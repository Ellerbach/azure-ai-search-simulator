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
/// Tests for suggester validation: as in Azure, an index has at most one suggester, and its source fields can be
/// string or string collection fields, including sub-fields of complex fields.
/// </summary>
public class SuggesterValidationTests
{
    private readonly IndexService _sut;

    public SuggesterValidationTests()
    {
        var repositoryMock = new Mock<IIndexRepository>();
        repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<SearchIndex>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SearchIndex idx, CancellationToken _) => idx);
        repositoryMock
            .Setup(r => r.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchIndex>());

        var luceneSettings = Options.Create(new LuceneSettings { IndexPath = Path.Combine(Path.GetTempPath(), "sim-test-" + Guid.NewGuid().ToString("N")) });
        var luceneManager = new LuceneIndexManager(new Mock<ILogger<LuceneIndexManager>>().Object, luceneSettings);

        _sut = new IndexService(repositoryMock.Object, Options.Create(new SimulatorSettings()), luceneManager, new Mock<ILogger<IndexService>>().Object);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("tags")]
    [InlineData("ShortName/en")]
    [InlineData("ShortName/Nested/fr")]
    [InlineData("ShortName/de")]
    public async Task CreateIndex_SuggesterOnStringOrSubField_Succeeds(string sourceField)
    {
        // Arrange
        var index = CreateIndex(new Suggester { Name = "sg", SourceFields = [sourceField] });

        // Act
        var result = await _sut.CreateIndexAsync(index);

        // Assert
        Assert.Equal(sourceField, Assert.Single(Assert.Single(result.Suggesters!).SourceFields));
    }

    [Theory]
    [InlineData("ShortName")]
    [InlineData("ShortName/missing")]
    [InlineData("rating")]
    [InlineData("code")]
    [InlineData("ShortName/keyword")]
    [InlineData("ShortName/custom")]
    public async Task CreateIndex_SuggesterOnUnknownOrNonStringField_Throws(string sourceField)
    {
        // Arrange
        var index = CreateIndex(new Suggester { Name = "sg", SourceFields = [sourceField] });

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateIndexAsync(index));

        // Assert
        Assert.Contains(sourceField, ex.Message);
    }

    [Fact]
    public async Task CreateIndex_TwoSuggesters_Throws()
    {
        // Arrange
        var index = CreateIndex(
            new Suggester { Name = "sg1", SourceFields = ["title"] },
            new Suggester { Name = "sg2", SourceFields = ["tags"] });

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateIndexAsync(index));

        // Assert
        Assert.Contains("more than one suggester", ex.Message);
    }

    private static SearchIndex CreateIndex(params Suggester[] suggesters) => new()
    {
        Name = "test-index",
        Fields =
        [
            new SearchField { Name = "id", Type = "Edm.String", Key = true },
            new SearchField { Name = "title", Type = "Edm.String", Searchable = true },
            new SearchField { Name = "tags", Type = "Collection(Edm.String)", Searchable = true },
            new SearchField { Name = "rating", Type = "Edm.Double" },
            new SearchField { Name = "code", Type = "Edm.String", Searchable = false },
            new SearchField
            {
                Name = "ShortName",
                Type = "Edm.ComplexType",
                Fields =
                [
                    new SearchField { Name = "en", Type = "Edm.String", Searchable = true },
                    new SearchField { Name = "de", Type = "Edm.String", Searchable = true, Analyzer = "de.microsoft" },
                    new SearchField { Name = "keyword", Type = "Edm.String", Searchable = true, Analyzer = "keyword" },
                    new SearchField { Name = "custom", Type = "Edm.String", Searchable = true, Analyzer = "my_analyzer" },
                    new SearchField
                    {
                        Name = "Nested",
                        Type = "Edm.ComplexType",
                        Fields = [new SearchField { Name = "fr", Type = "Edm.String", Searchable = true }],
                    },
                ],
            },
        ],
        Suggesters = [.. suggesters],
    };
}
