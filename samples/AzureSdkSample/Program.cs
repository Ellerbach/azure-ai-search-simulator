// Azure SDK Sample - Testing Azure AI Search Simulator with Azure.Search.Documents SDK
// This sample demonstrates that the simulator is compatible with the official Azure SDK.

using System.Text.Json.Serialization;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;

Console.WriteLine("=== Azure AI Search Simulator - Azure SDK Compatibility Test ===\n");

// Configuration - Point to the simulator
// Note: Azure SDK requires HTTPS. Use localhost HTTPS endpoint.
var endpoint = new Uri("https://localhost:7250");
var apiKey = new AzureKeyCredential("admin-key-12345");

// Configure client options to skip certificate validation (for local development only!)
var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
};
var clientOptions = new SearchClientOptions
{
    Transport = new Azure.Core.Pipeline.HttpClientTransport(handler)
};

// Create clients
var indexClient = new SearchIndexClient(endpoint, apiKey, clientOptions);
var indexerClient = new SearchIndexerClient(endpoint, apiKey, clientOptions);
var indexName = "hotels-sdk-test";

try
{
    // =====================================================
    // 1. Create Index
    // =====================================================
    Console.WriteLine("1. Creating index...");
    
    var fields = new FieldBuilder().Build(typeof(Hotel));
    var index = new SearchIndex(indexName, fields);

    await indexClient.CreateOrUpdateIndexAsync(index);
    Console.WriteLine($"   Index '{indexName}' created successfully.\n");

    // =====================================================
    // 2. Upload Documents
    // =====================================================
    Console.WriteLine("2. Uploading documents...");
    
    var searchClient = indexClient.GetSearchClient(indexName);
    
    var hotels = new[]
    {
        new
        {
            hotelId = "1",
            hotelName = "Grand Hotel Seattle",
            description = "A luxury hotel in downtown Seattle with stunning views of the Puget Sound and Olympic Mountains.",
            category = "Luxury",
            rating = 4.8,
            pricePerNight = 350.0,
            address = new { streetAddress = "123 Pike Street", city = "Seattle", country = "USA" },
            tags = new[] { "pool", "spa", "wifi", "restaurant", "concierge" }
        },
        new
        {
            hotelId = "2",
            hotelName = "Budget Inn Portland",
            description = "Affordable accommodation near Portland International Airport. Clean rooms with basic amenities.",
            category = "Budget",
            rating = 3.5,
            pricePerNight = 89.0,
            address = new { streetAddress = "456 Airport Way", city = "Portland", country = "USA" },
            tags = new[] { "wifi", "parking", "breakfast" }
        },
        new
        {
            hotelId = "3",
            hotelName = "Mountain Resort Aspen",
            description = "Ski-in ski-out resort with breathtaking mountain views. Perfect for winter sports enthusiasts.",
            category = "Resort",
            rating = 4.9,
            pricePerNight = 550.0,
            address = new { streetAddress = "789 Ski Run Road", city = "Aspen", country = "USA" },
            tags = new[] { "ski", "spa", "restaurant", "gym", "pool" }
        },
        new
        {
            hotelId = "4",
            hotelName = "Business Suites Chicago",
            description = "Modern business hotel in the heart of Chicago's financial district. Meeting rooms and business center available.",
            category = "Business",
            rating = 4.2,
            pricePerNight = 220.0,
            address = new { streetAddress = "100 LaSalle Street", city = "Chicago", country = "USA" },
            tags = new[] { "wifi", "business center", "meeting rooms", "restaurant" }
        },
        new
        {
            hotelId = "5",
            hotelName = "Beachfront Paradise Miami",
            description = "Beautiful beachfront hotel with direct access to Miami Beach. Tropical atmosphere and excellent dining.",
            category = "Resort",
            rating = 4.6,
            pricePerNight = 420.0,
            address = new { streetAddress = "200 Ocean Drive", city = "Miami", country = "USA" },
            tags = new[] { "beach", "pool", "spa", "restaurant", "bar" }
        }
    };

    var batch = IndexDocumentsBatch.Upload(hotels);
    await searchClient.IndexDocumentsAsync(batch);
    Console.WriteLine($"   Uploaded {hotels.Length} documents.\n");

    // Wait a moment for indexing
    await Task.Delay(500);

    // =====================================================
    // 3. Simple Search
    // =====================================================
    Console.WriteLine("3. Simple search for 'luxury hotel'...");
    
    var searchOptions = new SearchOptions
    {
        IncludeTotalCount = true,
        Select = { "hotelId", "hotelName", "rating", "category" }
    };
    
    var searchResults = await searchClient.SearchAsync<SearchDocument>("luxury hotel", searchOptions);
    
    Console.WriteLine($"   Found {searchResults.Value.TotalCount} results:");
    await foreach (var result in searchResults.Value.GetResultsAsync())
    {
        Console.WriteLine($"   - {result.Document["hotelName"]} (Score: {result.Score:F2})");
    }
    Console.WriteLine();

    // =====================================================
    // 4. Filtered Search
    // =====================================================
    Console.WriteLine("4. Search with filter (rating >= 4.5)...");
    
    var filteredOptions = new SearchOptions
    {
        Filter = "rating ge 4.5",
        OrderBy = { "rating desc" },
        Select = { "hotelId", "hotelName", "rating", "category" }
    };
    
    var filteredResults = await searchClient.SearchAsync<SearchDocument>("*", filteredOptions);
    
    Console.WriteLine("   High-rated hotels:");
    await foreach (var result in filteredResults.Value.GetResultsAsync())
    {
        Console.WriteLine($"   - {result.Document["hotelName"]} (Rating: {result.Document["rating"]})");
    }
    Console.WriteLine();

    // =====================================================
    // 5. Faceted Search
    // =====================================================
    Console.WriteLine("5. Faceted search...");
    
    var facetOptions = new SearchOptions
    {
        Facets = { "category,count:10", "tags,count:10" },
        Size = 0 // We only want facets, not results
    };
    
    var facetResults = await searchClient.SearchAsync<SearchDocument>("*", facetOptions);
    
    Console.WriteLine("   Categories:");
    if (facetResults.Value.Facets.TryGetValue("category", out var categoryFacets))
    {
        foreach (var facet in categoryFacets)
        {
            Console.WriteLine($"   - {facet.Value}: {facet.Count}");
        }
    }
    
    Console.WriteLine("\n   Tags:");
    if (facetResults.Value.Facets.TryGetValue("tags", out var tagFacets))
    {
        foreach (var facet in tagFacets.Take(5))
        {
            Console.WriteLine($"   - {facet.Value}: {facet.Count}");
        }
    }
    Console.WriteLine();

    // =====================================================
    // 6. Get Document by Key
    // =====================================================
    Console.WriteLine("6. Get document by key...");
    
    var document = await searchClient.GetDocumentAsync<SearchDocument>("3");
    Console.WriteLine($"   Retrieved: {document.Value["hotelName"]}");
    Console.WriteLine($"   Category: {document.Value["category"]}");
    Console.WriteLine($"   Rating: {document.Value["rating"]}\n");

    // =====================================================
    // 7. Document Count
    // =====================================================
    Console.WriteLine("7. Get document count...");
    
    var countResponse = await searchClient.GetDocumentCountAsync();
    Console.WriteLine($"   Total documents: {countResponse.Value}\n");

    // =====================================================
    // 8. Update Document (Merge)
    // =====================================================
    Console.WriteLine("8. Merge document (update rating)...");
    
    var updateBatch = IndexDocumentsBatch.Merge(new[]
    {
        new SearchDocument { ["hotelId"] = "1", ["rating"] = 4.9 }
    });
    await searchClient.IndexDocumentsAsync(updateBatch);
    
    var updatedDoc = await searchClient.GetDocumentAsync<SearchDocument>("1");
    Console.WriteLine($"   Updated rating: {updatedDoc.Value["rating"]}\n");

    // =====================================================
    // 9. Delete Document
    // =====================================================
    Console.WriteLine("9. Delete document...");
    
    var deleteBatch = IndexDocumentsBatch.Delete("hotelId", new[] { "2" });
    await searchClient.IndexDocumentsAsync(deleteBatch);
    
    var newCount = await searchClient.GetDocumentCountAsync();
    Console.WriteLine($"   Documents after delete: {newCount.Value}\n");

    // =====================================================
    // 10. Create Data Source (for Indexer)
    // =====================================================
    Console.WriteLine("10. Creating data source...");
    
    var dataSourceName = "blob-datasource-sdk-test";
    var dataSource = new SearchIndexerDataSourceConnection(
        name: dataSourceName,
        type: SearchIndexerDataSourceType.AzureBlob,
        connectionString: "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=mykey;EndpointSuffix=core.windows.net",
        container: new SearchIndexerDataContainer("documents"))
    {
        Description = "Test blob data source created via Azure SDK"
    };
    
    await indexerClient.CreateOrUpdateDataSourceConnectionAsync(dataSource);
    Console.WriteLine($"   Data source '{dataSourceName}' created.\n");

    // =====================================================
    // 11. Create Skillset (Cognitive Skills)
    // =====================================================
    Console.WriteLine("11. Creating skillset...");
    
    var skillsetName = "document-skillset-sdk-test";
    
    var ocrSkill = new OcrSkill(
        inputs: new[] { new InputFieldMappingEntry("image") { Source = "/document/normalized_images/*" } },
        outputs: new[] { new OutputFieldMappingEntry("text") { TargetName = "extractedText" } })
    {
        Name = "ocr-skill",
        Description = "Extract text from images",
        Context = "/document/normalized_images/*"
    };
    
    var mergeSkill = new MergeSkill(
        inputs: new[]
        {
            new InputFieldMappingEntry("text") { Source = "/document/content" },
            new InputFieldMappingEntry("itemsToInsert") { Source = "/document/normalized_images/*/extractedText" }
        },
        outputs: new[] { new OutputFieldMappingEntry("mergedText") { TargetName = "merged_content" } })
    {
        Name = "merge-skill",
        Description = "Merge content with OCR text",
        Context = "/document"
    };
    
    var skillset = new SearchIndexerSkillset(
        name: skillsetName,
        skills: new SearchIndexerSkill[] { ocrSkill, mergeSkill })
    {
        Description = "Skillset for document processing created via Azure SDK"
    };
    
    await indexerClient.CreateOrUpdateSkillsetAsync(skillset);
    Console.WriteLine($"   Skillset '{skillsetName}' created.\n");

    // =====================================================
    // 12. Create Index for Indexer
    // =====================================================
    Console.WriteLine("12. Creating index for indexer...");
    
    var indexerTargetIndex = "documents-index-sdk-test";
    var documentIndex = new SearchIndex(indexerTargetIndex)
    {
        Fields =
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
            new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.Values.EnLucene },
            new SearchableField("merged_content") { AnalyzerName = LexicalAnalyzerName.Values.EnLucene },
            new SimpleField("metadata_storage_path", SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("metadata_storage_name", SearchFieldDataType.String) { IsFilterable = true }
        }
    };
    
    await indexClient.CreateOrUpdateIndexAsync(documentIndex);
    Console.WriteLine($"   Index '{indexerTargetIndex}' created.\n");

    // =====================================================
    // 13. Create Indexer
    // =====================================================
    Console.WriteLine("13. Creating indexer...");
    
    var indexerName = "blob-indexer-sdk-test";
    var indexer = new SearchIndexer(
        name: indexerName,
        dataSourceName: dataSourceName,
        targetIndexName: indexerTargetIndex)
    {
        Description = "Indexer for blob storage created via Azure SDK",
        SkillsetName = skillsetName,
        Schedule = new IndexingSchedule(TimeSpan.FromHours(1)),
        Parameters = new IndexingParameters
        {
            BatchSize = 10,
            MaxFailedItems = 5,
            MaxFailedItemsPerBatch = 2,
            Configuration =
            {
                ["parsingMode"] = "default",
                ["dataToExtract"] = "contentAndMetadata"
            }
        },
        FieldMappings =
        {
            new FieldMapping("metadata_storage_path") { TargetFieldName = "id" }
        },
        OutputFieldMappings =
        {
            new FieldMapping("/document/merged_content") { TargetFieldName = "merged_content" }
        }
    };
    
    await indexerClient.CreateOrUpdateIndexerAsync(indexer);
    Console.WriteLine($"   Indexer '{indexerName}' created.\n");

    // =====================================================
    // 14. Get Indexer Status
    // =====================================================
    Console.WriteLine("14. Getting indexer status...");
    
    var indexerStatus = await indexerClient.GetIndexerStatusAsync(indexerName);
    Console.WriteLine($"   Indexer status: {indexerStatus.Value.Status}");
    Console.WriteLine($"   Last result: {indexerStatus.Value.LastResult?.Status.ToString() ?? "No runs yet"}\n");

    // =====================================================
    // 15. List All Resources
    // =====================================================
    Console.WriteLine("15. Listing all resources...");
    
    Console.WriteLine("   Indexes:");
    await foreach (var idx in indexClient.GetIndexNamesAsync())
    {
        Console.WriteLine($"   - {idx}");
    }
    
    Console.WriteLine("\n   Data Sources:");
    var dataSources = await indexerClient.GetDataSourceConnectionNamesAsync();
    foreach (var ds in dataSources.Value)
    {
        Console.WriteLine($"   - {ds}");
    }
    
    Console.WriteLine("\n   Indexers:");
    var indexers = await indexerClient.GetIndexerNamesAsync();
    foreach (var idxr in indexers.Value)
    {
        Console.WriteLine($"   - {idxr}");
    }
    
    Console.WriteLine("\n   Skillsets:");
    var skillsets = await indexerClient.GetSkillsetNamesAsync();
    foreach (var ss in skillsets.Value)
    {
        Console.WriteLine($"   - {ss}");
    }
    Console.WriteLine();

    // =====================================================
    // 16. Run Indexer Manually
    // =====================================================
    Console.WriteLine("16. Running indexer manually...");
    
    await indexerClient.RunIndexerAsync(indexerName);
    Console.WriteLine($"   Indexer '{indexerName}' run triggered.\n");

    // =====================================================
    // 17. Reset Indexer
    // =====================================================
    Console.WriteLine("17. Resetting indexer...");
    
    await indexerClient.ResetIndexerAsync(indexerName);
    Console.WriteLine($"   Indexer '{indexerName}' reset.\n");

    // =====================================================
    // 18. Cleanup - Delete All Resources
    // =====================================================
    Console.WriteLine("18. Cleaning up - deleting all resources...");
    
    // Delete indexer first (depends on data source and skillset)
    await indexerClient.DeleteIndexerAsync(indexerName);
    Console.WriteLine($"   Indexer '{indexerName}' deleted.");
    
    // Delete skillset
    await indexerClient.DeleteSkillsetAsync(skillsetName);
    Console.WriteLine($"   Skillset '{skillsetName}' deleted.");
    
    // Delete data source
    await indexerClient.DeleteDataSourceConnectionAsync(dataSourceName);
    Console.WriteLine($"   Data source '{dataSourceName}' deleted.");
    
    // Delete document index
    await indexClient.DeleteIndexAsync(indexerTargetIndex);
    Console.WriteLine($"   Index '{indexerTargetIndex}' deleted.");
    
    // Delete hotel index
    await indexClient.DeleteIndexAsync(indexName);
    Console.WriteLine($"   Index '{indexName}' deleted.\n");

    // =====================================================
    // Summary
    // =====================================================
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("=== All Azure SDK operations completed successfully! ===");
    Console.WriteLine("The simulator is compatible with Azure.Search.Documents SDK.");
    Console.WriteLine();
    Console.WriteLine("Demonstrated features:");
    Console.WriteLine("  ✓ Index CRUD operations");
    Console.WriteLine("  ✓ Document operations (upload, merge, delete, get)");
    Console.WriteLine("  ✓ Search (simple, filtered, faceted)");
    Console.WriteLine("  ✓ Data Source CRUD operations");
    Console.WriteLine("  ✓ Skillset CRUD operations");
    Console.WriteLine("  ✓ Indexer CRUD operations (create, run, reset, status)");
    Console.ResetColor();
}
catch (RequestFailedException ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Azure SDK Error: {ex.Status} - {ex.Message}");
    Console.ResetColor();
    
    // Try to clean up all resources
    try
    {
        await indexerClient.DeleteIndexerAsync("blob-indexer-sdk-test");
        await indexerClient.DeleteSkillsetAsync("document-skillset-sdk-test");
        await indexerClient.DeleteDataSourceConnectionAsync("blob-datasource-sdk-test");
        await indexClient.DeleteIndexAsync("documents-index-sdk-test");
        await indexClient.DeleteIndexAsync(indexName);
    }
    catch { }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Error: {ex.Message}");
    Console.ResetColor();
    
    // Try to clean up all resources
    try
    {
        await indexerClient.DeleteIndexerAsync("blob-indexer-sdk-test");
        await indexerClient.DeleteSkillsetAsync("document-skillset-sdk-test");
        await indexerClient.DeleteDataSourceConnectionAsync("blob-datasource-sdk-test");
        await indexClient.DeleteIndexAsync("documents-index-sdk-test");
        await indexClient.DeleteIndexAsync(indexName);
    }
    catch { }
}

// =====================================================
// Hotel Model Class for FieldBuilder
// =====================================================
public class Hotel
{
    [SimpleField(IsKey = true, IsFilterable = true)]
    public string? HotelId { get; set; }

    [SearchableField(IsFilterable = true, IsSortable = true)]
    public string? HotelName { get; set; }

    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.EnLucene)]
    public string? Description { get; set; }

    [SearchableField(IsFilterable = true, IsFacetable = true)]
    public string? Category { get; set; }

    [SimpleField(IsFilterable = true, IsSortable = true, IsFacetable = true)]
    public double? Rating { get; set; }

    [SimpleField(IsFilterable = true, IsSortable = true)]
    public double? PricePerNight { get; set; }

    public Address? Address { get; set; }

    [SearchableField(IsFilterable = true, IsFacetable = true)]
    public string[]? Tags { get; set; }
}

public class Address
{
    [SearchableField]
    public string? StreetAddress { get; set; }

    [SearchableField(IsFilterable = true, IsFacetable = true)]
    public string? City { get; set; }

    [SearchableField(IsFilterable = true)]
    public string? Country { get; set; }
}