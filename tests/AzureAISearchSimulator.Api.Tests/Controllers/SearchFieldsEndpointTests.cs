using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AzureAISearchSimulator.Api.Tests.Controllers;

/// <summary>
/// End-to-end tests for searchFields on the search endpoints, run through the real HTTP pipeline
/// so the GET query-string binding and the 400 mapping are covered.
/// </summary>
[Collection(SimulatorHostCollection.Name)]
public class SearchFieldsEndpointTests : IClassFixture<SearchFieldsEndpointTests.SimulatorFactory>
{
    private const string AdminKey = "searchfields-tests-admin-key";
    private const string ApiVersion = "2024-07-01";

    private readonly HttpClient _client;

    public SearchFieldsEndpointTests(SimulatorFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("api-key", AdminKey);
    }

    public class SimulatorFactory : WebApplicationFactory<Program>
    {
        private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "searchfields-endpoint-tests", Guid.NewGuid().ToString());

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
        var indexName = $"searchfields-{Guid.NewGuid():N}";

        var createResponse = await _client.PutAsJsonAsync($"/indexes/{indexName}?api-version={ApiVersion}", new
        {
            name = indexName,
            fields = new object[]
            {
                new { name = "hotelId", type = "Edm.String", key = true },
                new { name = "hotelName", type = "Edm.String", searchable = true },
                new { name = "description", type = "Edm.String", searchable = true }
            }
        });
        Assert.True(createResponse.IsSuccessStatusCode, await createResponse.Content.ReadAsStringAsync());

        var uploadResponse = await _client.PostAsJsonAsync($"/indexes/{indexName}/docs/index?api-version={ApiVersion}", new
        {
            value = new[]
            {
                new Dictionary<string, object> { ["@search.action"] = "upload", ["hotelId"] = "1", ["hotelName"] = "Ocean Retreat", ["description"] = "Quiet rooms near the harbor" },
                new Dictionary<string, object> { ["@search.action"] = "upload", ["hotelId"] = "2", ["hotelName"] = "Harbor Inn", ["description"] = "Every room has an ocean view" }
            }
        });
        Assert.True(uploadResponse.IsSuccessStatusCode, await uploadResponse.Content.ReadAsStringAsync());

        return indexName;
    }

    [Fact]
    public async Task GetSearch_WithSearchFields_MatchesOnlyThoseFields()
    {
        var indexName = await CreateIndexWithDocuments();

        var response = await _client.GetAsync(
            $"/indexes/{indexName}/docs?api-version={ApiVersion}&search=ocean&searchFields=description");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        var value = json.RootElement.GetProperty("value");
        Assert.Equal(1, value.GetArrayLength());
        Assert.Equal("2", value[0].GetProperty("hotelId").GetString());
    }

    [Fact]
    public async Task PostSearch_WithUnknownSearchField_ReturnsBadRequest()
    {
        var indexName = await CreateIndexWithDocuments();

        var response = await _client.PostAsJsonAsync($"/indexes/{indexName}/docs/search?api-version={ApiVersion}", new
        {
            search = "ocean",
            searchFields = "nope"
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Unknown field 'nope' in search field list.", body);
    }
}
