using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AzureAISearchSimulator.Api.Tests.Controllers;

/// <summary>
/// End-to-end tests for the continuation of a search without top: as in Azure, @odata.nextLink points at the
/// endpoint that was called, POST adds @search.nextPageParameters, and GET gets a link it can follow with GET.
/// </summary>
[Collection(SimulatorHostCollection.Name)]
public class NextPageEndpointTests : IClassFixture<NextPageEndpointTests.SimulatorFactory>
{
    private const string AdminKey = "nextpage-tests-admin-key";
    private const string ApiVersion = "2024-07-01";

    private readonly HttpClient _client;

    public NextPageEndpointTests(SimulatorFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("api-key", AdminKey);
    }

    public class SimulatorFactory : WebApplicationFactory<Program>
    {
        private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "nextpage-endpoint-tests", Guid.NewGuid().ToString());

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

    [Fact]
    public async Task PostSearch_WithoutTop_LinksToTheSameEndpointWithNextPageParameters()
    {
        // Arrange
        var indexName = await CreateIndexWithDocuments(60);
        var url = $"/indexes/{indexName}/docs/search?api-version={ApiVersion}";

        // Act
        using var json = await ReadJson(await _client.PostAsJsonAsync(url, new { search = "*" }));

        // Assert
        var root = json.RootElement;
        Assert.Equal(50, root.GetProperty("value").GetArrayLength());
        Assert.Equal(new Uri(_client.BaseAddress!, url).ToString(), root.GetProperty("@odata.nextLink").GetString());
        Assert.Equal(50, root.GetProperty("@search.nextPageParameters").GetProperty("skip").GetInt32());
    }

    [Fact]
    public async Task GetSearch_WithoutTop_LinksToTheNextPageWithGet()
    {
        // Arrange
        var indexName = await CreateIndexWithDocuments(60);

        // Act
        using var first = await ReadJson(await _client.GetAsync($"/indexes/{indexName}/docs?api-version={ApiVersion}&search=*&$orderby=id"));
        var nextLink = first.RootElement.GetProperty("@odata.nextLink").GetString()!;
        using var second = await ReadJson(await _client.GetAsync(nextLink));

        // Assert
        Assert.False(first.RootElement.TryGetProperty("@search.nextPageParameters", out var parameters) && parameters.ValueKind != JsonValueKind.Null);
        Assert.Contains("$skip=50", Uri.UnescapeDataString(nextLink));
        var ids = first.RootElement.GetProperty("value").EnumerateArray()
            .Concat(second.RootElement.GetProperty("value").EnumerateArray())
            .Select(d => d.GetProperty("id").GetString())
            .ToList();
        Assert.Equal(60, ids.Distinct().Count());
        Assert.False(second.RootElement.TryGetProperty("@odata.nextLink", out var last) && last.ValueKind != JsonValueKind.Null);
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonDocument.Parse(body);
    }

    private async Task<string> CreateIndexWithDocuments(int count)
    {
        var indexName = $"nextpage-{Guid.NewGuid():N}";
        var create = await _client.PutAsJsonAsync($"/indexes/{indexName}?api-version={ApiVersion}", new
        {
            name = indexName,
            fields = new object[] { new { name = "id", type = "Edm.String", key = true, sortable = true } }
        });
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());

        var upload = await _client.PostAsJsonAsync($"/indexes/{indexName}/docs/index?api-version={ApiVersion}", new
        {
            value = Enumerable.Range(0, count)
                .Select(i => new Dictionary<string, object> { ["@search.action"] = "upload", ["id"] = $"d{i:D3}" })
                .ToArray()
        });
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
        return indexName;
    }
}
