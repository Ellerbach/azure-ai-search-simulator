using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AzureAISearchSimulator.Api.Tests.Controllers;

/// <summary>
/// End-to-end tests for the suggest endpoints, run through the real routing pipeline
/// so route conflicts (e.g. GET docs/suggest vs. GET docs/{key}) are caught.
/// </summary>
public class SuggestEndpointTests : IClassFixture<SuggestEndpointTests.SimulatorFactory>
{
    private const string AdminKey = "suggest-tests-admin-key";
    private const string ApiVersion = "2024-07-01";

    private readonly HttpClient _client;

    public SuggestEndpointTests(SimulatorFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("api-key", AdminKey);
    }

    public class SimulatorFactory : WebApplicationFactory<Program>
    {
        private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "suggest-endpoint-tests", Guid.NewGuid().ToString());

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("SimulatorSettings:DataDirectory", _dataDir);
            builder.UseSetting("SimulatorSettings:AdminApiKey", AdminKey);
            builder.UseSetting("LuceneSettings:IndexPath", Path.Combine(_dataDir, "lucene"));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { Directory.Delete(_dataDir, true); } catch { }
        }
    }

    private async Task<string> CreateIndexWithDocuments()
    {
        var indexName = $"suggest-{Guid.NewGuid():N}";

        var createResponse = await _client.PutAsJsonAsync($"/indexes/{indexName}?api-version={ApiVersion}", new
        {
            name = indexName,
            fields = new object[]
            {
                new { name = "Id", type = "Edm.String", key = true },
                new { name = "PurchaserLastName", type = "Edm.String", searchable = true },
                new { name = "LocationCode", type = "Edm.String", filterable = true }
            },
            suggesters = new[]
            {
                new { name = "sg", searchMode = "analyzingInfixMatching", sourceFields = new[] { "PurchaserLastName" } }
            }
        });
        Assert.True(createResponse.IsSuccessStatusCode, await createResponse.Content.ReadAsStringAsync());

        var uploadResponse = await _client.PostAsJsonAsync($"/indexes/{indexName}/docs/index?api-version={ApiVersion}", new
        {
            value = new[]
            {
                new Dictionary<string, object> { ["@search.action"] = "upload", ["Id"] = "1", ["PurchaserLastName"] = "Smithson", ["LocationCode"] = "0705" },
                new Dictionary<string, object> { ["@search.action"] = "upload", ["Id"] = "2", ["PurchaserLastName"] = "Smirnov", ["LocationCode"] = "0001" }
            }
        });
        Assert.True(uploadResponse.IsSuccessStatusCode, await uploadResponse.Content.ReadAsStringAsync());

        return indexName;
    }

    private static async Task<JsonElement> AssertSingleSuggestion(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);

        using var json = JsonDocument.Parse(body);
        var value = json.RootElement.GetProperty("value");
        Assert.Equal(1, value.GetArrayLength());
        return value[0].Clone();
    }

    [Fact]
    public async Task PostSuggest_WithFilterAndHighlightTags_ReturnsFilteredHighlightedSuggestion()
    {
        var indexName = await CreateIndexWithDocuments();

        var response = await _client.PostAsJsonAsync($"/indexes/{indexName}/docs/suggest?api-version={ApiVersion}", new
        {
            search = "smi",
            suggesterName = "sg",
            filter = "LocationCode eq '0705'",
            highlightPreTag = "<b>",
            highlightPostTag = "</b>"
        });

        var suggestion = await AssertSingleSuggestion(response);
        Assert.Equal("<b>Smi</b>thson", suggestion.GetProperty("@search.text").GetString());
        Assert.Equal("1", suggestion.GetProperty("Id").GetString());
        Assert.False(suggestion.TryGetProperty("PurchaserLastName", out _), "Only the key is returned when select is omitted");
    }

    [Fact]
    public async Task GetSuggest_IsRoutedToSuggestNotDocumentLookup()
    {
        var indexName = await CreateIndexWithDocuments();

        var response = await _client.GetAsync(
            $"/indexes/{indexName}/docs/suggest?api-version={ApiVersion}&search=smi&suggesterName=sg" +
            $"&$filter={Uri.EscapeDataString("LocationCode eq '0705'")}" +
            $"&highlightPreTag={Uri.EscapeDataString("<b>")}&highlightPostTag={Uri.EscapeDataString("</b>")}" +
            "&$select=Id,LocationCode");

        var suggestion = await AssertSingleSuggestion(response);
        Assert.Equal("<b>Smi</b>thson", suggestion.GetProperty("@search.text").GetString());
        Assert.Equal("0705", suggestion.GetProperty("LocationCode").GetString());
    }

    [Fact]
    public async Task GetAutocomplete_IsRoutedToAutocompleteNotDocumentLookup()
    {
        var indexName = await CreateIndexWithDocuments();

        var response = await _client.GetAsync(
            $"/indexes/{indexName}/docs/autocomplete?api-version={ApiVersion}&search=smi&suggesterName=sg");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        Assert.Contains("\"value\"", body);
    }

    [Fact]
    public async Task PostSuggest_WithUnknownSuggester_ReturnsBadRequest()
    {
        var indexName = await CreateIndexWithDocuments();

        var response = await _client.PostAsJsonAsync($"/indexes/{indexName}/docs/suggest?api-version={ApiVersion}", new
        {
            search = "smi",
            suggesterName = "missing"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
