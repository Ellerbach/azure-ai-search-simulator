using AzureAISearchSimulator.Api.Services;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Storage.Repositories;
using Microsoft.Extensions.Logging;
using Moq;

namespace AzureAISearchSimulator.Api.Tests.Services;

/// <summary>
/// Unit tests for SynonymMapService - synonym rule parsing, validation, and query-time expansion.
/// </summary>
public class SynonymMapServiceTests
{
    private readonly Mock<ISynonymMapRepository> _repositoryMock;
    private readonly SynonymMapService _service;

    public SynonymMapServiceTests()
    {
        _repositoryMock = new Mock<ISynonymMapRepository>();
        _service = new SynonymMapService(
            _repositoryMock.Object,
            Mock.Of<ILogger<SynonymMapService>>());
    }

    #region Synonym Rule Parsing

    [Fact]
    public void ParseSynonymRules_EquivalentSynonyms_CreatesBidirectionalMappings()
    {
        var rules = SynonymMapService.ParseSynonymRules("usa, united states, america");

        Assert.Contains("usa", rules.Keys);
        Assert.Contains("united states", rules.Keys);
        Assert.Contains("america", rules.Keys);

        // Each term maps to the other two
        Assert.Contains("united states", rules["usa"]);
        Assert.Contains("america", rules["usa"]);
        Assert.Contains("usa", rules["united states"]);
        Assert.Contains("america", rules["united states"]);
        Assert.Contains("usa", rules["america"]);
        Assert.Contains("united states", rules["america"]);
    }

    [Fact]
    public void ParseSynonymRules_ExplicitMapping_CreatesUnidirectionalMapping()
    {
        var rules = SynonymMapService.ParseSynonymRules("wa, washington => washington state");

        // Left-side terms should map to right-side
        Assert.Contains("wa", rules.Keys);
        Assert.Contains("washington", rules.Keys);
        Assert.Contains("washington state", rules["wa"]);
        Assert.Contains("washington state", rules["washington"]);

        // Right-side should NOT map back to left-side
        Assert.DoesNotContain("washington state", rules.Keys);
    }

    [Fact]
    public void ParseSynonymRules_MultipleRules_ParsedCorrectly()
    {
        var synonyms = "usa, united states\nca, california\nautomobile => car, vehicle";
        var rules = SynonymMapService.ParseSynonymRules(synonyms);

        Assert.Contains("usa", rules.Keys);
        Assert.Contains("united states", rules.Keys);
        Assert.Contains("ca", rules.Keys);
        Assert.Contains("california", rules.Keys);
        Assert.Contains("automobile", rules.Keys);

        Assert.Contains("car", rules["automobile"]);
        Assert.Contains("vehicle", rules["automobile"]);
    }

    [Fact]
    public void ParseSynonymRules_EmptyInput_ReturnsEmptyDictionary()
    {
        var rules = SynonymMapService.ParseSynonymRules("");
        Assert.Empty(rules);
    }

    [Fact]
    public void ParseSynonymRules_CommentsAndBlankLines_AreIgnored()
    {
        var synonyms = "# This is a comment\nusa, united states\n\n# Another comment\nca, california";
        var rules = SynonymMapService.ParseSynonymRules(synonyms);

        Assert.Equal(4, rules.Count); // usa, united states, ca, california
    }

    [Fact]
    public void ParseSynonymRules_SingleTerm_IsSkipped()
    {
        var rules = SynonymMapService.ParseSynonymRules("lonely");
        Assert.Empty(rules);
    }

    [Fact]
    public void ParseSynonymRules_CaseInsensitive()
    {
        var rules = SynonymMapService.ParseSynonymRules("USA, United States");

        Assert.Contains("usa", rules.Keys);
        Assert.Contains("united states", rules.Keys);
    }

    #endregion

    #region Term Expansion

    [Fact]
    public async Task ExpandTerms_WithMatchingSynonymMap_ReturnsExpandedTerms()
    {
        var synonymMap = new SynonymMap
        {
            Name = "my-synonyms",
            Format = "solr",
            Synonyms = "usa, united states, america"
        };

        _repositoryMock.Setup(x => x.GetByNameAsync("my-synonyms"))
            .ReturnsAsync(synonymMap);

        var result = _service.ExpandTerms(new[] { "my-synonyms" }, "usa");

        Assert.Contains("usa", result);
        Assert.Contains("united states", result);
        Assert.Contains("america", result);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ExpandTerms_NoMatchingSynonym_ReturnsOriginalTerm()
    {
        _repositoryMock.Setup(x => x.GetByNameAsync("my-synonyms"))
            .ReturnsAsync(new SynonymMap
            {
                Name = "my-synonyms",
                Format = "solr",
                Synonyms = "cat, feline"
            });

        var result = _service.ExpandTerms(new[] { "my-synonyms" }, "dog");

        Assert.Single(result);
        Assert.Contains("dog", result);
    }

    [Fact]
    public void ExpandTerms_NonExistentSynonymMap_ReturnsOriginalTerm()
    {
        _repositoryMock.Setup(x => x.GetByNameAsync("missing"))
            .ReturnsAsync((SynonymMap?)null);

        var result = _service.ExpandTerms(new[] { "missing" }, "test");

        Assert.Single(result);
        Assert.Contains("test", result);
    }

    [Fact]
    public void ExpandTerms_ExplicitMapping_ExpandsCorrectDirection()
    {
        _repositoryMock.Setup(x => x.GetByNameAsync("my-synonyms"))
            .ReturnsAsync(new SynonymMap
            {
                Name = "my-synonyms",
                Format = "solr",
                Synonyms = "automobile => car, vehicle"
            });

        // Forward direction should expand
        var result = _service.ExpandTerms(new[] { "my-synonyms" }, "automobile");
        Assert.Contains("car", result);
        Assert.Contains("vehicle", result);
        Assert.Contains("automobile", result);

        // Reverse direction should NOT expand
        var result2 = _service.ExpandTerms(new[] { "my-synonyms" }, "car");
        Assert.Single(result2);
        Assert.Contains("car", result2);
    }

    #endregion

    #region CRUD Validation

    [Fact]
    public async Task CreateAsync_ValidSynonymMap_Succeeds()
    {
        var synonymMap = new SynonymMap
        {
            Name = "test-synonyms",
            Format = "solr",
            Synonyms = "usa, united states"
        };

        _repositoryMock.Setup(x => x.ExistsAsync("test-synonyms")).ReturnsAsync(false);
        _repositoryMock.Setup(x => x.CreateAsync(synonymMap)).ReturnsAsync(synonymMap);

        var result = await _service.CreateAsync(synonymMap);

        Assert.Equal("test-synonyms", result.Name);
        _repositoryMock.Verify(x => x.CreateAsync(synonymMap), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsInvalidOperationException()
    {
        var synonymMap = new SynonymMap
        {
            Name = "existing",
            Format = "solr",
            Synonyms = "a, b"
        };

        _repositoryMock.Setup(x => x.ExistsAsync("existing")).ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateAsync(synonymMap));
    }

    [Fact]
    public async Task CreateAsync_EmptyName_ThrowsArgumentException()
    {
        var synonymMap = new SynonymMap
        {
            Name = "",
            Format = "solr",
            Synonyms = "a, b"
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CreateAsync(synonymMap));
    }

    [Fact]
    public async Task CreateAsync_InvalidFormat_ThrowsArgumentException()
    {
        var synonymMap = new SynonymMap
        {
            Name = "test",
            Format = "invalid",
            Synonyms = "a, b"
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CreateAsync(synonymMap));
    }

    [Fact]
    public async Task CreateAsync_EmptySynonyms_ThrowsArgumentException()
    {
        var synonymMap = new SynonymMap
        {
            Name = "test",
            Format = "solr",
            Synonyms = ""
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CreateAsync(synonymMap));
    }

    [Fact]
    public async Task CreateAsync_InvalidName_ThrowsArgumentException()
    {
        var synonymMap = new SynonymMap
        {
            Name = "123invalid",
            Format = "solr",
            Synonyms = "a, b"
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CreateAsync(synonymMap));
    }

    [Fact]
    public async Task CreateOrUpdateAsync_ExistingMap_Updates()
    {
        var synonymMap = new SynonymMap
        {
            Name = "test",
            Format = "solr",
            Synonyms = "a, b, c"
        };

        _repositoryMock.Setup(x => x.ExistsAsync("test")).ReturnsAsync(true);
        _repositoryMock.Setup(x => x.UpdateAsync(synonymMap)).ReturnsAsync(synonymMap);

        var result = await _service.CreateOrUpdateAsync("test", synonymMap);

        _repositoryMock.Verify(x => x.UpdateAsync(synonymMap), Times.Once);
        _repositoryMock.Verify(x => x.CreateAsync(It.IsAny<SynonymMap>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrUpdateAsync_NewMap_Creates()
    {
        var synonymMap = new SynonymMap
        {
            Name = "test",
            Format = "solr",
            Synonyms = "a, b, c"
        };

        _repositoryMock.Setup(x => x.ExistsAsync("test")).ReturnsAsync(false);
        _repositoryMock.Setup(x => x.CreateAsync(synonymMap)).ReturnsAsync(synonymMap);

        var result = await _service.CreateOrUpdateAsync("test", synonymMap);

        _repositoryMock.Verify(x => x.CreateAsync(synonymMap), Times.Once);
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<SynonymMap>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ExistingMap_InvalidatesCache()
    {
        // First, load synonym map into cache by expanding terms
        _repositoryMock.Setup(x => x.GetByNameAsync("cached-map"))
            .ReturnsAsync(new SynonymMap
            {
                Name = "cached-map",
                Format = "solr",
                Synonyms = "old, ancient"
            });

        var result1 = _service.ExpandTerms(new[] { "cached-map" }, "old");
        Assert.Contains("ancient", result1);

        // Now delete and recreate with different synonyms
        _repositoryMock.Setup(x => x.DeleteAsync("cached-map")).ReturnsAsync(true);
        await _service.DeleteAsync("cached-map");

        // Update mock to return new synonyms
        _repositoryMock.Setup(x => x.GetByNameAsync("cached-map"))
            .ReturnsAsync(new SynonymMap
            {
                Name = "cached-map",
                Format = "solr",
                Synonyms = "new, modern"
            });

        var result2 = _service.ExpandTerms(new[] { "cached-map" }, "old");
        // After cache invalidation, old synonyms should not appear
        Assert.DoesNotContain("ancient", result2);
    }

    #endregion
}
