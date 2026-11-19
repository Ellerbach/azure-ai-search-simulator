using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Search;
using AzureAISearchSimulator.Search.Hnsw;

namespace AzureAISearchSimulator.Integration.Tests;

/// <summary>
/// Integration tests for the OData <c>$filter</c> parser and evaluator: each filter returns exactly the matching
/// documents (conditions inside one lambda hold for the same collection element, at any depth), and a filter the
/// simulator can't evaluate exactly is rejected rather than ignored.
/// </summary>
public class ODataFilterIntegrationTests : IDisposable
{
    private const string Now = "2026-09-23T00:00:00Z";

    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;

    public ODataFilterIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "odata-filter-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);

        _luceneManager = new LuceneIndexManager(
            Mock.Of<ILogger<LuceneIndexManager>>(),
            Options.Create(new LuceneSettings { IndexPath = _testDir }));

        _indexServiceMock = new Mock<IIndexService>();

        _documentService = new DocumentService(
            Mock.Of<ILogger<DocumentService>>(),
            _luceneManager,
            Mock.Of<IVectorSearchService>(),
            _indexServiceMock.Object);

        _searchService = new SearchService(
            Mock.Of<ILogger<SearchService>>(),
            _luceneManager,
            Mock.Of<IVectorSearchService>(),
            _indexServiceMock.Object,
            Mock.Of<ISynonymMapResolver>(),
            new ScoringProfileService(Mock.Of<ILogger<ScoringProfileService>>()));
    }

    [Theory]
    [InlineData("ClientIdentifier eq 'acme_42' and EmployeeStatuses/any(s: s/GroupXRefCode eq 'ACTIVE' and s/EffectiveStart lt " + Now + " and (s/EffectiveEnd ge " + Now + " or s/EffectiveEnd eq null)) and WorkAssignments/any(w: w/EffectiveStart lt " + Now + " and (w/EffectiveEnd ge " + Now + " or w/EffectiveEnd eq null))", "p1")]
    [InlineData("EmployeeStatuses/any(s: s/GroupXRefCode eq 'ACTIVE' and s/EffectiveEnd eq null)", "p1,p3,p4,p5")]
    [InlineData("EmployeeStatuses/all(s: s/GroupXRefCode eq 'ACTIVE')", "p1,p3,p4,p5")]
    [InlineData("WorkAssignments/any(w: w/PositionManagements/any(p: p/PositionId eq 7 and p/EffectiveEnd eq null))", "p1,p5")]
    [InlineData("WorkAssignments/any(w: w/EffectiveEnd eq null and w/PositionManagements/any(p: p/PositionId eq 7))", "p1,p5")]
    [InlineData("WorkAssignments/any(w: not w/PositionManagements/any())", "p4")]
    [InlineData("not WorkAssignments/any()", "p3")]
    [InlineData("ClientIdentifier eq 'acme_42' and Roles/any(r: r eq '1004')", "p1")]
    [InlineData("Roles/any(r: r eq '1005' or r eq '1004')", "p1,p2,p5")]
    [InlineData("Roles/all(r: r ne '1005')", "p1,p3,p4,p5")]
    [InlineData("Roles/any(r: search.in(r, '1006|9', '|'))", "p2")]
    [InlineData("HubAudiences/any(a: a eq 45 or a eq 46)", "p1,p2")]
    [InlineData("not PageAudiences/any() or PageAudiences/any(a: a eq 88)", "p1,p2,p3,p4,p5")]
    [InlineData("(not PageAudiences/any())", "p1,p3,p4,p5")]
    [InlineData("false", "")]
    [InlineData("true and IsReleased", "p1,p5")]
    [InlineData("IsReleased eq false or IsReleased eq null", "p2,p3,p4")]
    [InlineData("Rating ne 5", "p2,p3,p4,p5")]
    [InlineData("Rating ge 3 and Rating lt 5", "p2")]
    [InlineData("5 le Rating", "p1")]
    [InlineData("search.in(ClientIdentifier, 'other_1, nobody_2')", "p5")]
    [InlineData("search.in(ClientIdentifier, 'acme_42!x', '!') and id gt 'p3'", "p4")]
    [InlineData("not search.ismatch('EmployeeProfile.*', 'TargetObjectType', 'full', 'any') and ClientIdentifier eq 'acme_42'", "p1,p3,p4")]
    [InlineData("search.ismatch('admin', 'Title')", "p1,p5")]
    [InlineData("ClientIdentifier eq 'o''brien'", "")]
    [InlineData("EmployeeStatuses/any(s: s/EffectiveStart eq 2020-01-01T00:00:00.0004999Z)", "p1,p3,p5")]
    [InlineData("EmployeeStatuses/any(s: s/EffectiveStart lt 2019-01-01T00:00:00.001+00:00)", "p2")]
    public async Task SearchAsync_WithFilter_ReturnsExactlyTheMatchingDocuments(string filter, string expected)
    {
        // Arrange
        var index = await SeedAsync();

        // Act
        var ids = await IdsAsync(index, filter);

        // Assert
        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries), ids);
    }

    [Fact]
    public async Task SearchAsync_WithFilterAndSearchText_ReturnsOnlyDocumentsMatchingBoth()
    {
        // Arrange
        var index = await SeedAsync();

        // Act
        var ids = await IdsAsync(index, "ClientIdentifier eq 'acme_42' and Roles/any(r: r eq '1004')", "payroll");

        // Assert
        Assert.Equal(["p1"], ids);
    }

    [Theory]
    [InlineData("Unknown eq 'x'")]
    [InlineData("not Rating gt 5")]
    [InlineData("Roles eq '1004'")]
    [InlineData("Roles/any(r: r ne '1004')")]
    [InlineData("Roles/all(r: r eq '1004')")]
    [InlineData("Roles/any(r: r eq '1004' and r eq '1005')")]
    [InlineData("WorkAssignments/EffectiveEnd eq null")]
    [InlineData("WorkAssignments/any(w: search.ismatch('x'))")]
    [InlineData("Title eq 'x'")]
    [InlineData("Rating eq '5'")]
    [InlineData("EmployeeStatuses/any(s: s/EffectiveEnd ge '2026-01-01')")]
    [InlineData("EmployeeStatuses/any(s: s/EffectiveEnd ge 2026-01-01)")]
    [InlineData("IsReleased gt true")]
    [InlineData("Rating lt null")]
    [InlineData("geo.distance(Location, geography'POINT(0 0)') lt 5")]
    [InlineData("search.ismatchscoring('x')")]
    [InlineData("ClientIdentifier eq 'acme_42' foo")]
    [InlineData("ClientIdentifier eq 'acme_42")]
    [InlineData("ClientIdentifier eq 'a' AND Rating eq 5")]
    [InlineData("(ClientIdentifier eq 'a'")]
    [InlineData("ClientIdentifier eq Rating")]
    [InlineData("Roles/any(r: s eq '1')")]
    [InlineData("search.ismatch('[unclosed', 'Title', 'full')")]
    [InlineData("search.in(Rating, '1,2')")]
    [InlineData("WorkAssignments/any(w: w/PositionManagements/any(w: w/PositionId eq 7))")]
    public async Task SearchAsync_WithFilterItCannotEvaluateExactly_ThrowsArgumentException(string filter)
    {
        // Arrange
        var index = await SeedAsync();

        // Act
        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => _searchService.SearchAsync(index, new SearchRequest { Search = "*", Filter = filter }));

        // Assert
        Assert.Contains("filter", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SearchField Field(string name, string type, bool filterable = true, bool searchable = false, params SearchField[] fields) =>
        new()
        {
            Name = name,
            Type = type,
            Filterable = type.Contains("Complex") ? null : filterable,
            Searchable = searchable,
            Fields = fields.Length > 0 ? fields.ToList() : null,
        };

    private static Dictionary<string, object?> Span(string? start, string? end, params (string Name, object? Value)[] extra)
    {
        var span = new Dictionary<string, object?> { ["EffectiveStart"] = start, ["EffectiveEnd"] = end };
        foreach (var (name, value) in extra)
        {
            span[name] = value;
        }

        return span;
    }

    private async Task<string> SeedAsync()
    {
        var name = $"people-{Guid.NewGuid():N}";
        var span = new[] { Field("EffectiveStart", "Edm.DateTimeOffset"), Field("EffectiveEnd", "Edm.DateTimeOffset") };
        var index = new SearchIndex
        {
            Name = name,
            Fields =
            [
                new SearchField { Name = "id", Type = "Edm.String", Key = true, Filterable = true },
                Field("ClientIdentifier", "Edm.String"),
                Field("Title", "Edm.String", filterable: false, searchable: true),
                Field("TargetObjectType", "Edm.String", filterable: true, searchable: true),
                Field("Roles", "Collection(Edm.String)"),
                Field("HubAudiences", "Collection(Edm.Int32)"),
                Field("PageAudiences", "Collection(Edm.Int32)"),
                Field("IsReleased", "Edm.Boolean"),
                Field("Rating", "Edm.Int32"),
                Field("EmployeeStatuses", "Collection(Edm.ComplexType)", fields: [Field("GroupXRefCode", "Edm.String"), .. span]),
                Field("WorkAssignments", "Collection(Edm.ComplexType)", fields:
                [
                    .. span,
                    Field("PositionManagements", "Collection(Edm.ComplexType)", fields: [Field("PositionId", "Edm.Int32"), .. span]),
                ]),
            ],
        };
        _indexServiceMock.Setup(x => x.GetIndexAsync(name, It.IsAny<CancellationToken>())).ReturnsAsync(index);
        _luceneManager.GetWriter(name);

        var documents = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["id"] = "p1", ["ClientIdentifier"] = "acme_42", ["Title"] = "Payroll admin", ["TargetObjectType"] = "Feature",
                ["Roles"] = new[] { "1004" }, ["HubAudiences"] = new[] { 45 }, ["IsReleased"] = true, ["Rating"] = 5,
                ["EmployeeStatuses"] = new[] { Span("2020-01-01T00:00:00Z", null, ("GroupXRefCode", "ACTIVE")) },
                ["WorkAssignments"] = new[]
                {
                    Span("2020-01-01T00:00:00Z", null, ("PositionManagements", new[] { Span("2020-01-01T00:00:00Z", null, ("PositionId", 7)) })),
                },
            },
            new()
            {
                // ACTIVE has ended and the current status isn't ACTIVE: matches only if elements get mixed up.
                ["id"] = "p2", ["ClientIdentifier"] = "acme_42", ["Title"] = "Payroll clerk", ["TargetObjectType"] = "EmployeeProfile.Summary",
                ["Roles"] = new[] { "1005", "1006" }, ["HubAudiences"] = new[] { 46 }, ["PageAudiences"] = new[] { 88 },
                ["IsReleased"] = false, ["Rating"] = 3,
                ["EmployeeStatuses"] = new[]
                {
                    Span("2019-01-01T00:00:00Z", "2021-01-01T00:00:00Z", ("GroupXRefCode", "ACTIVE")),
                    Span("2021-01-01T00:00:00Z", null, ("GroupXRefCode", "TERMINATED")),
                },
                ["WorkAssignments"] = new[]
                {
                    Span("2019-01-01T00:00:00Z", "2021-01-01T00:00:00Z", ("PositionManagements", new[] { Span("2019-01-01T00:00:00Z", "2021-01-01T00:00:00Z", ("PositionId", 7)) })),
                    Span("2021-01-01T00:00:00Z", null, ("PositionManagements", new[] { Span("2021-01-01T00:00:00Z", null, ("PositionId", 8)) })),
                },
            },
            new()
            {
                // Active, but no work assignments or positions at all.
                ["id"] = "p3", ["ClientIdentifier"] = "acme_42", ["Title"] = "Payroll", ["Roles"] = Array.Empty<string>(),
                ["EmployeeStatuses"] = new[] { Span("2020-01-01T00:00:00Z", null, ("GroupXRefCode", "ACTIVE")) },
            },
            new()
            {
                // A work assignment without positions, and one starting in the future.
                ["id"] = "p4", ["ClientIdentifier"] = "acme_42", ["Title"] = "Payroll",
                ["EmployeeStatuses"] = new[] { Span(null, null, ("GroupXRefCode", "ACTIVE")) },
                ["WorkAssignments"] = new[] { Span("2027-01-01T00:00:00Z", null), Span("2020-01-01T00:00:00Z", "2020-06-01T00:00:00Z") },
            },
            new()
            {
                ["id"] = "p5", ["ClientIdentifier"] = "other_1", ["Title"] = "Payroll admin", ["Roles"] = new[] { "1004" }, ["IsReleased"] = true,
                ["EmployeeStatuses"] = new[] { Span("2020-01-01T00:00:00Z", null, ("GroupXRefCode", "ACTIVE")) },
                ["WorkAssignments"] = new[]
                {
                    Span("2020-01-01T00:00:00Z", null, ("PositionManagements", new[] { Span("2020-01-01T00:00:00Z", null, ("PositionId", 7)) })),
                },
            },
        };

        await _documentService.IndexDocumentsAsync(name, new IndexDocumentsRequest
        {
            Value = documents.Select(document =>
            {
                var action = new IndexAction { ["@search.action"] = "upload" };
                foreach (var kvp in document)
                {
                    action[kvp.Key] = kvp.Value;
                }

                return action;
            }).ToList(),
        });

        return name;
    }

    private async Task<string[]> IdsAsync(string index, string filter, string search = "*")
    {
        var response = await _searchService.SearchAsync(index, new SearchRequest { Search = search, Filter = filter, QueryType = "full" });
        return response.Value.Select(r => r["id"]!.ToString()!).Order().ToArray();
    }

    public void Dispose()
    {
        _luceneManager.Dispose();
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }
}
