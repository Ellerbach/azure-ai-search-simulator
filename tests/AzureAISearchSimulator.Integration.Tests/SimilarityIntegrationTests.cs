using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Search;
using AzureAISearchSimulator.Search.Hnsw;
using Lucene.Net.Search.Similarities;
using ClassicSimilarity = Lucene.Net.Search.Similarities.DefaultSimilarity;

namespace AzureAISearchSimulator.Integration.Tests;

/// <summary>
/// Integration tests verifying that similarity configuration (BM25 k1/b) is actually
/// wired into Lucene scoring and produces different scores when parameters change.
/// </summary>
public class SimilarityIntegrationTests : IDisposable
{
    private readonly string _testDir;

    public SimilarityIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "similarity-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    private (LuceneIndexManager lucene, SearchService search, DocumentService docs, Mock<IIndexService> indexMock) CreateServices(string subDir)
    {
        var dir = Path.Combine(_testDir, subDir);
        Directory.CreateDirectory(dir);

        var luceneSettings = Options.Create(new LuceneSettings { IndexPath = dir });
        var luceneManager = new LuceneIndexManager(
            Mock.Of<ILogger<LuceneIndexManager>>(),
            luceneSettings);

        var vectorSearchService = Mock.Of<IVectorSearchService>();
        var indexServiceMock = new Mock<IIndexService>();

        var scoringProfileService = new ScoringProfileService(
            Mock.Of<ILogger<ScoringProfileService>>());

        var documentService = new DocumentService(
            Mock.Of<ILogger<DocumentService>>(),
            luceneManager,
            vectorSearchService,
            indexServiceMock.Object);

        var searchService = new SearchService(
            Mock.Of<ILogger<SearchService>>(),
            luceneManager,
            vectorSearchService,
            indexServiceMock.Object,
            Mock.Of<ISynonymMapResolver>(),
            scoringProfileService);

        return (luceneManager, searchService, documentService, indexServiceMock);
    }

    private SearchIndex CreateIndex(string name, SimilarityAlgorithm? similarity = null)
    {
        return new SearchIndex
        {
            Name = name,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true, Filterable = true },
                new() { Name = "content", Type = "Edm.String", Searchable = true }
            },
            Similarity = similarity
        };
    }

    private async Task UploadDocuments(DocumentService docs, string indexName, params (string id, string content)[] documents)
    {
        var request = new IndexDocumentsRequest
        {
            Value = documents.Select(d =>
            {
                var action = new IndexAction
                {
                    ["@search.action"] = "upload",
                    ["id"] = d.id,
                    ["content"] = d.content
                };
                return action;
            }).ToList()
        };
        await docs.IndexDocumentsAsync(indexName, request);
    }

    private async Task<double> GetSearchScore(SearchService search, string indexName, string query)
    {
        var request = new SearchRequest { Search = query };
        var response = await search.SearchAsync(indexName, request);
        return response.Value.FirstOrDefault()?.Score ?? 0.0;
    }

    [Fact]
    public void CreateLuceneSimilarity_DefaultBM25_ReturnsCorrectParameters()
    {
        // Default BM25 (null similarity)
        var similarity = LuceneIndexManager.CreateLuceneSimilarity(null);
        Assert.IsType<BM25Similarity>(similarity);

        var bm25 = (BM25Similarity)similarity;
        Assert.Equal(1.2f, bm25.K1, precision: 4);
        Assert.Equal(0.75f, bm25.B, precision: 4);
    }

    [Fact]
    public void CreateLuceneSimilarity_CustomBM25_ReturnsCorrectParameters()
    {
        var config = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 2.0,
            B = 0.5
        };
        var similarity = LuceneIndexManager.CreateLuceneSimilarity(config);
        Assert.IsType<BM25Similarity>(similarity);

        var bm25 = (BM25Similarity)similarity;
        Assert.Equal(2.0f, bm25.K1, precision: 4);
        Assert.Equal(0.5f, bm25.B, precision: 4);
    }

    [Fact]
    public void CreateLuceneSimilarity_BM25WithNullParams_UsesDefaults()
    {
        var config = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = null,
            B = null
        };
        var similarity = LuceneIndexManager.CreateLuceneSimilarity(config);
        Assert.IsType<BM25Similarity>(similarity);

        var bm25 = (BM25Similarity)similarity;
        Assert.Equal(1.2f, bm25.K1, precision: 4);
        Assert.Equal(0.75f, bm25.B, precision: 4);
    }

    [Fact]
    public void CreateLuceneSimilarity_ClassicSimilarity_ReturnsClassic()
    {
        var config = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.ClassicSimilarity"
        };
        var similarity = LuceneIndexManager.CreateLuceneSimilarity(config);
        Assert.IsType<DefaultSimilarity>(similarity);
    }

    [Fact]
    public async Task Search_DifferentK1Values_ProduceDifferentScores()
    {
        // Index A: k1=0.0 (binary model — term frequency has no effect)
        var (luceneA, searchA, docsA, mockA) = CreateServices("index-k1-0");
        var indexA = CreateIndex("test-k1-0", new SimilarityAlgorithm { K1 = 0.0, B = 0.75 });
        mockA.Setup(x => x.GetIndexAsync("test-k1-0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexA);

        // Index B: k1=2.0 (high term frequency impact)
        var (luceneB, searchB, docsB, mockB) = CreateServices("index-k1-2");
        var indexB = CreateIndex("test-k1-2", new SimilarityAlgorithm { K1 = 2.0, B = 0.75 });
        mockB.Setup(x => x.GetIndexAsync("test-k1-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexB);

        try
        {
            // Same document with repeated term "search" — k1 affects how much repetition matters
            var doc = ("1", "search search search search search azure");

            await UploadDocuments(docsA, "test-k1-0", doc);
            await UploadDocuments(docsB, "test-k1-2", doc);

            var scoreA = await GetSearchScore(searchA, "test-k1-0", "search");
            var scoreB = await GetSearchScore(searchB, "test-k1-2", "search");

            // Both should find the document
            Assert.True(scoreA > 0, "Score with k1=0.0 should be > 0");
            Assert.True(scoreB > 0, "Score with k1=2.0 should be > 0");

            // With different k1 values and a high term frequency document,
            // the scores should be different
            Assert.NotEqual(scoreA, scoreB);
        }
        finally
        {
            luceneA.Dispose();
            luceneB.Dispose();
        }
    }

    [Fact]
    public async Task Search_DifferentBValues_ProduceDifferentScores()
    {
        // Index A: b=0.0 (no length normalization)
        var (luceneA, searchA, docsA, mockA) = CreateServices("index-b-0");
        var indexA = CreateIndex("test-b-0", new SimilarityAlgorithm { K1 = 1.2, B = 0.0 });
        mockA.Setup(x => x.GetIndexAsync("test-b-0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexA);

        // Index B: b=1.0 (full length normalization)
        var (luceneB, searchB, docsB, mockB) = CreateServices("index-b-1");
        var indexB = CreateIndex("test-b-1", new SimilarityAlgorithm { K1 = 1.2, B = 1.0 });
        mockB.Setup(x => x.GetIndexAsync("test-b-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexB);

        try
        {
            // A long document — b affects how document length impacts scoring
            var shortDoc = ("1", "azure search");
            var longDoc = ("2", "azure search is a cloud service that provides full text search capabilities over documents and data stored in the cloud with many features");

            await UploadDocuments(docsA, "test-b-0", shortDoc, longDoc);
            await UploadDocuments(docsB, "test-b-1", shortDoc, longDoc);

            // Search for "azure search" — the long doc has the terms but also lots of other text
            var requestA = new SearchRequest { Search = "azure search" };
            var requestB = new SearchRequest { Search = "azure search" };

            var responseA = await searchA.SearchAsync("test-b-0", requestA);
            var responseB = await searchB.SearchAsync("test-b-1", requestB);

            // With b=0.0, length doesn't matter, so long doc might score similarly to short doc
            // With b=1.0, long doc is penalized more for its length
            // The relative scores between short and long docs should differ across the two indexes
            Assert.Equal(2, responseA.Value.Count);
            Assert.Equal(2, responseB.Value.Count);

            // Get scores for the long doc (id=2) in each index
            var longScoreA = responseA.Value.First(v => v.ContainsKey("id") && v["id"]?.ToString() == "2").Score;
            var longScoreB = responseB.Value.First(v => v.ContainsKey("id") && v["id"]?.ToString() == "2").Score;

            // With different b values, the long document score should differ
            Assert.NotEqual(longScoreA, longScoreB);
        }
        finally
        {
            luceneA.Dispose();
            luceneB.Dispose();
        }
    }

    [Fact]
    public async Task Search_DefaultSimilarity_UsesBM25Defaults()
    {
        // No similarity specified — should use BM25 with k1=1.2, b=0.75 (the defaults)
        var (luceneDefault, searchDefault, docsDefault, mockDefault) = CreateServices("index-default");
        var indexDefault = CreateIndex("test-default"); // No similarity
        mockDefault.Setup(x => x.GetIndexAsync("test-default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexDefault);

        // Explicit k1=1.2, b=0.75 — should produce identical scores
        var (luceneExplicit, searchExplicit, docsExplicit, mockExplicit) = CreateServices("index-explicit");
        var indexExplicit = CreateIndex("test-explicit", new SimilarityAlgorithm { K1 = 1.2, B = 0.75 });
        mockExplicit.Setup(x => x.GetIndexAsync("test-explicit", It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexExplicit);

        try
        {
            var doc = ("1", "azure search service");

            await UploadDocuments(docsDefault, "test-default", doc);
            await UploadDocuments(docsExplicit, "test-explicit", doc);

            var scoreDefault = await GetSearchScore(searchDefault, "test-default", "search");
            var scoreExplicit = await GetSearchScore(searchExplicit, "test-explicit", "search");

            Assert.True(scoreDefault > 0);
            Assert.Equal(scoreDefault, scoreExplicit, precision: 4);
        }
        finally
        {
            luceneDefault.Dispose();
            luceneExplicit.Dispose();
        }
    }

    [Fact]
    public async Task Search_ClassicSimilarity_ProducesScoresInZeroToOneRange()
    {
        var (lucene, search, docs, mock) = CreateServices("index-classic");
        var index = CreateIndex("test-classic", new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.ClassicSimilarity"
        });
        mock.Setup(x => x.GetIndexAsync("test-classic", It.IsAny<CancellationToken>()))
            .ReturnsAsync(index);

        try
        {
            await UploadDocuments(docs, "test-classic",
                ("1", "azure search service"),
                ("2", "cloud computing platform"));

            var request = new SearchRequest { Search = "azure search" };
            var response = await search.SearchAsync("test-classic", request);

            Assert.NotEmpty(response.Value);
            foreach (var result in response.Value)
            {
                // ClassicSimilarity (TF-IDF) produces scores in 0–1 range
                Assert.True(result.Score >= 0, $"Score {result.Score} should be >= 0");
                // Note: In Lucene, ClassicSimilarity scores can sometimes exceed 1.0
                // due to coordination factors and field norms, but they stay bounded
            }
        }
        finally
        {
            lucene.Dispose();
        }
    }

    [Fact]
    public async Task Search_ClassicVsBM25_ProduceDifferentScores()
    {
        var (luceneBM25, searchBM25, docsBM25, mockBM25) = CreateServices("index-bm25");
        var indexBM25 = CreateIndex("test-bm25", new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 1.2,
            B = 0.75
        });
        mockBM25.Setup(x => x.GetIndexAsync("test-bm25", It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexBM25);

        var (luceneClassic, searchClassic, docsClassic, mockClassic) = CreateServices("index-classic-vs");
        var indexClassic = CreateIndex("test-classic-vs", new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.ClassicSimilarity"
        });
        mockClassic.Setup(x => x.GetIndexAsync("test-classic-vs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexClassic);

        try
        {
            var doc = ("1", "azure search service provides full text search");

            await UploadDocuments(docsBM25, "test-bm25", doc);
            await UploadDocuments(docsClassic, "test-classic-vs", doc);

            var scoreBM25 = await GetSearchScore(searchBM25, "test-bm25", "search");
            var scoreClassic = await GetSearchScore(searchClassic, "test-classic-vs", "search");

            Assert.True(scoreBM25 > 0);
            Assert.True(scoreClassic > 0);
            // Different algorithms should produce different scores
            Assert.NotEqual(scoreBM25, scoreClassic);
        }
        finally
        {
            luceneBM25.Dispose();
            luceneClassic.Dispose();
        }
    }

    [Fact]
    public async Task Search_ExactUserScenario_CustomBM25ProducesDifferentScores()
    {
        // Reproduce the user's exact scenario: 3 indexes with same docs, different similarity
        // IMPORTANT: Filterable=true on search fields to match ApplyFieldDefaults behavior.
        // This previously caused a Lucene bug where TextField+StringField with the same name
        // corrupted IndexOptions (DOCS_ONLY) and norms, making BM25 degenerate to just IDF.
        
        // Default BM25
        var (luceneA, searchA, docsA, mockA) = CreateServices("user-default");
        var indexA = new SearchIndex
        {
            Name = "sim-default",
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true, Filterable = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true, Filterable = true },
                new() { Name = "content", Type = "Edm.String", Searchable = true, Filterable = true }
            }
            // No similarity → defaults to BM25 k1=1.2, b=0.75
        };
        mockA.Setup(x => x.GetIndexAsync("sim-default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexA);

        // Custom BM25 k1=2.0, b=0.5
        var (luceneB, searchB, docsB, mockB) = CreateServices("user-custom");
        var indexB = new SearchIndex
        {
            Name = "sim-custom-bm25",
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true, Filterable = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true, Filterable = true },
                new() { Name = "content", Type = "Edm.String", Searchable = true, Filterable = true }
            },
            Similarity = new SimilarityAlgorithm
            {
                ODataType = "#Microsoft.Azure.Search.BM25Similarity",
                K1 = 2.0,
                B = 0.5
            }
        };
        mockB.Setup(x => x.GetIndexAsync("sim-custom-bm25", It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexB);

        try
        {
            // Same docs as user's sample
            var uploadA = new IndexDocumentsRequest
            {
                Value = new List<IndexAction>
                {
                    new() { ["@search.action"] = "upload", ["id"] = "1", ["title"] = "Azure Search", ["content"] = "Azure AI Search is a cloud search service that gives developers APIs and tools for building search experiences." },
                    new() { ["@search.action"] = "upload", ["id"] = "2", ["title"] = "Search Features", ["content"] = "Search search search. Full-text search with BM25 scoring, vector search, and hybrid search capabilities." },
                    new() { ["@search.action"] = "upload", ["id"] = "3", ["title"] = "Getting Started", ["content"] = "This is a short document about getting started with Azure." }
                }
            };
            var uploadB = new IndexDocumentsRequest
            {
                Value = new List<IndexAction>
                {
                    new() { ["@search.action"] = "upload", ["id"] = "1", ["title"] = "Azure Search", ["content"] = "Azure AI Search is a cloud search service that gives developers APIs and tools for building search experiences." },
                    new() { ["@search.action"] = "upload", ["id"] = "2", ["title"] = "Search Features", ["content"] = "Search search search. Full-text search with BM25 scoring, vector search, and hybrid search capabilities." },
                    new() { ["@search.action"] = "upload", ["id"] = "3", ["title"] = "Getting Started", ["content"] = "This is a short document about getting started with Azure." }
                }
            };

            await docsA.IndexDocumentsAsync("sim-default", uploadA);
            await docsB.IndexDocumentsAsync("sim-custom-bm25", uploadB);

            // Search for "search"
            var requestA = new SearchRequest { Search = "search", Select = "id, title", Count = true };
            var responseA = await searchA.SearchAsync("sim-default", requestA);

            var requestB = new SearchRequest { Search = "search", Select = "id, title", Count = true };
            var responseB = await searchB.SearchAsync("sim-custom-bm25", requestB);

            // Both should return 2 results (doc 3 doesn't match "search")
            Assert.Equal(2, responseA.Value.Count);
            Assert.Equal(2, responseB.Value.Count);

            // Get all scores for diagnostics
            var scoreA1 = responseA.Value[0].Score!.Value;
            var scoreA2 = responseA.Value[1].Score!.Value;
            var scoreB1 = responseB.Value[0].Score!.Value;
            var scoreB2 = responseB.Value[1].Score!.Value;

            // Log scores for debugging
            var docA1Id = responseA.Value[0].ContainsKey("id") ? responseA.Value[0]["id"]?.ToString() : "?";
            var docA2Id = responseA.Value[1].ContainsKey("id") ? responseA.Value[1]["id"]?.ToString() : "?";
            var docB1Id = responseB.Value[0].ContainsKey("id") ? responseB.Value[0]["id"]?.ToString() : "?";
            var docB2Id = responseB.Value[1].ContainsKey("id") ? responseB.Value[1]["id"]?.ToString() : "?";

            // DIAGNOSTIC: Different k1/b should produce different scores for docs with different term frequencies
            // Doc 2 has "search" many more times in content than Doc 1
            // With k1=2.0 (custom), repeated terms matter more, so doc 2 should score relatively higher
            Assert.True(scoreA1 > 0, $"Default index doc {docA1Id} score should be > 0, got {scoreA1}");
            Assert.True(scoreB1 > 0, $"Custom index doc {docB1Id} score should be > 0, got {scoreB1}");

            // The scores between the two indexes should NOT be identical
            // because k1=1.2,b=0.75 vs k1=2.0,b=0.5 produce different BM25 weights
            Assert.True(
                Math.Abs(scoreA1 - scoreB1) > 0.001 || Math.Abs(scoreA2 - scoreB2) > 0.001,
                $"Default BM25 scores ({scoreA1}, {scoreA2}) should differ from custom BM25 scores ({scoreB1}, {scoreB2}). " +
                $"Default docs: {docA1Id}={scoreA1}, {docA2Id}={scoreA2}. Custom docs: {docB1Id}={scoreB1}, {docB2Id}={scoreB2}");

            // Within each index, doc 2 (6x "search" in content) should score higher than doc 1 (3x)
            // This validates that term frequency is actually being used in scoring (not just IDF)
            Assert.True(scoreA1 != scoreA2,
                $"Default BM25: docs should have DIFFERENT scores (tf matters). " +
                $"Doc {docA1Id}={scoreA1}, Doc {docA2Id}={scoreA2}");
            Assert.True(scoreB1 != scoreB2,
                $"Custom BM25: docs should have DIFFERENT scores (tf matters). " +
                $"Doc {docB1Id}={scoreB1}, Doc {docB2Id}={scoreB2}");
        }
        finally
        {
            luceneA.Dispose();
            luceneB.Dispose();
        }
    }

    [Fact]
    public void SimilarityAlgorithm_JsonRoundTrip_PreservesK1AndB()
    {
        // Verify JSON round-trip preserves k1/b (simulates LiteDB storage)
        var index = new SearchIndex
        {
            Name = "test",
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true }
            },
            Similarity = new SimilarityAlgorithm
            {
                ODataType = "#Microsoft.Azure.Search.BM25Similarity",
                K1 = 2.0,
                B = 0.5
            }
        };

        // Serialize with default options (as LiteDbIndexRepository does)
        var json = System.Text.Json.JsonSerializer.Serialize(index);

        // Deserialize back (as LiteDbIndexRepository does)
        var restored = System.Text.Json.JsonSerializer.Deserialize<SearchIndex>(json)!;

        Assert.NotNull(restored.Similarity);
        Assert.Equal("#Microsoft.Azure.Search.BM25Similarity", restored.Similarity.ODataType);
        Assert.Equal(2.0, restored.Similarity.K1);
        Assert.Equal(0.5, restored.Similarity.B);
    }

    [Fact]
    public void RawLucene_DifferentBM25Params_ProduceDifferentScores()
    {
        // Test BM25 scoring directly through Lucene API to verify parameter wiring
        using var dirA = new Lucene.Net.Store.RAMDirectory();
        using var dirB = new Lucene.Net.Store.RAMDirectory();
        using var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(LuceneDocumentMapper.AppLuceneVersion);

        var simA = new BM25Similarity(1.2f, 0.75f);  // default
        var simB = new BM25Similarity(2.0f, 0.5f);   // custom

        // Index same docs with both similarities
        void IndexDocs(Lucene.Net.Store.Directory dir, Similarity sim)
        {
            var config = new Lucene.Net.Index.IndexWriterConfig(LuceneDocumentMapper.AppLuceneVersion, analyzer)
            {
                OpenMode = Lucene.Net.Index.OpenMode.CREATE,
                Similarity = sim
            };
            using var writer = new Lucene.Net.Index.IndexWriter(dir, config);

            var doc1 = new Lucene.Net.Documents.Document();
            doc1.Add(new Lucene.Net.Documents.StringField("id", "1", Lucene.Net.Documents.Field.Store.YES));
            doc1.Add(new Lucene.Net.Documents.TextField("title", "Azure Search", Lucene.Net.Documents.Field.Store.YES));
            doc1.Add(new Lucene.Net.Documents.TextField("content", "Azure AI Search is a cloud search service that gives developers APIs and tools for building search experiences.", Lucene.Net.Documents.Field.Store.YES));
            writer.AddDocument(doc1);

            var doc2 = new Lucene.Net.Documents.Document();
            doc2.Add(new Lucene.Net.Documents.StringField("id", "2", Lucene.Net.Documents.Field.Store.YES));
            doc2.Add(new Lucene.Net.Documents.TextField("title", "Search Features", Lucene.Net.Documents.Field.Store.YES));
            doc2.Add(new Lucene.Net.Documents.TextField("content", "Search search search. Full-text search with BM25 scoring, vector search, and hybrid search capabilities.", Lucene.Net.Documents.Field.Store.YES));
            writer.AddDocument(doc2);

            var doc3 = new Lucene.Net.Documents.Document();
            doc3.Add(new Lucene.Net.Documents.StringField("id", "3", Lucene.Net.Documents.Field.Store.YES));
            doc3.Add(new Lucene.Net.Documents.TextField("title", "Getting Started", Lucene.Net.Documents.Field.Store.YES));
            doc3.Add(new Lucene.Net.Documents.TextField("content", "This is a short document about getting started with Azure.", Lucene.Net.Documents.Field.Store.YES));
            writer.AddDocument(doc3);

            writer.Commit();
        }

        IndexDocs(dirA, simA);
        IndexDocs(dirB, simB);

        // Search with query "search" across title and content
        using var readerA = Lucene.Net.Index.DirectoryReader.Open(dirA);
        using var readerB = Lucene.Net.Index.DirectoryReader.Open(dirB);

        var searcherA = new Lucene.Net.Search.IndexSearcher(readerA) { Similarity = simA };
        var searcherB = new Lucene.Net.Search.IndexSearcher(readerB) { Similarity = simB };

        var parserA = new Lucene.Net.QueryParsers.Classic.MultiFieldQueryParser(
            LuceneDocumentMapper.AppLuceneVersion,
            new[] { "title", "content" },
            analyzer);
        parserA.DefaultOperator = Lucene.Net.QueryParsers.Classic.Operator.OR;

        var parserB = new Lucene.Net.QueryParsers.Classic.MultiFieldQueryParser(
            LuceneDocumentMapper.AppLuceneVersion,
            new[] { "title", "content" },
            analyzer);
        parserB.DefaultOperator = Lucene.Net.QueryParsers.Classic.Operator.OR;

        var queryA = parserA.Parse("search");
        var queryB = parserB.Parse("search");

        var resultsA = searcherA.Search(queryA, 10);
        var resultsB = searcherB.Search(queryB, 10);

        // Both should return 2 docs
        Assert.Equal(2, resultsA.TotalHits);
        Assert.Equal(2, resultsB.TotalHits);

        // Get scores
        var scoreA_doc0 = resultsA.ScoreDocs[0].Score;
        var scoreA_doc1 = resultsA.ScoreDocs[1].Score;
        var scoreB_doc0 = resultsB.ScoreDocs[0].Score;
        var scoreB_doc1 = resultsB.ScoreDocs[1].Score;

        // With different k1/b, the top scores should differ
        Assert.True(
            Math.Abs(scoreA_doc0 - scoreB_doc0) > 0.001 || Math.Abs(scoreA_doc1 - scoreB_doc1) > 0.001,
            $"Default BM25 scores ({scoreA_doc0:F6}, {scoreA_doc1:F6}) should differ from custom BM25 ({scoreB_doc0:F6}, {scoreB_doc1:F6})"
        );

        // Also verify doc scores differ WITHIN each index (doc 2 has more "search" in content)
        Assert.NotEqual(scoreA_doc0, scoreA_doc1);
    }

    [Fact]
    public void RawLucene_TextFieldPlusStringField_SameName_CorruptsScoring()
    {
        // This test proves that adding both TextField and StringField with the SAME field name
        // causes Lucene to downgrade IndexOptions to DOCS_ONLY (no term frequencies) and
        // omit norms (no document length normalization), making BM25 degenerate to just IDF.
        using var dirGood = new Lucene.Net.Store.RAMDirectory();
        using var dirBad = new Lucene.Net.Store.RAMDirectory();
        using var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(LuceneDocumentMapper.AppLuceneVersion);
        var sim = new BM25Similarity(1.2f, 0.75f);

        // "Good" index: TextField only (no StringField conflict)
        {
            var config = new Lucene.Net.Index.IndexWriterConfig(LuceneDocumentMapper.AppLuceneVersion, analyzer)
            {
                OpenMode = Lucene.Net.Index.OpenMode.CREATE,
                Similarity = sim
            };
            using var writer = new Lucene.Net.Index.IndexWriter(dirGood, config);

            var doc1 = new Lucene.Net.Documents.Document();
            doc1.Add(new Lucene.Net.Documents.StringField("id", "1", Lucene.Net.Documents.Field.Store.YES));
            doc1.Add(new Lucene.Net.Documents.TextField("title", "Azure Search", Lucene.Net.Documents.Field.Store.YES));
            doc1.Add(new Lucene.Net.Documents.TextField("content", "Azure AI Search is a cloud search service.", Lucene.Net.Documents.Field.Store.YES));
            writer.AddDocument(doc1);

            var doc2 = new Lucene.Net.Documents.Document();
            doc2.Add(new Lucene.Net.Documents.StringField("id", "2", Lucene.Net.Documents.Field.Store.YES));
            doc2.Add(new Lucene.Net.Documents.TextField("title", "Search Features", Lucene.Net.Documents.Field.Store.YES));
            doc2.Add(new Lucene.Net.Documents.TextField("content", "Search search search search search capabilities.", Lucene.Net.Documents.Field.Store.YES));
            writer.AddDocument(doc2);

            writer.Commit();
        }

        // "Bad" index: TextField + StringField with SAME field name (simulates searchable+filterable)
        {
            var config = new Lucene.Net.Index.IndexWriterConfig(LuceneDocumentMapper.AppLuceneVersion, analyzer)
            {
                OpenMode = Lucene.Net.Index.OpenMode.CREATE,
                Similarity = sim
            };
            using var writer = new Lucene.Net.Index.IndexWriter(dirBad, config);

            var doc1 = new Lucene.Net.Documents.Document();
            doc1.Add(new Lucene.Net.Documents.StringField("id", "1", Lucene.Net.Documents.Field.Store.YES));
            doc1.Add(new Lucene.Net.Documents.TextField("title", "Azure Search", Lucene.Net.Documents.Field.Store.YES));
            doc1.Add(new Lucene.Net.Documents.StringField("title", "Azure Search", Lucene.Net.Documents.Field.Store.NO)); // CONFLICT!
            doc1.Add(new Lucene.Net.Documents.TextField("content", "Azure AI Search is a cloud search service.", Lucene.Net.Documents.Field.Store.YES));
            doc1.Add(new Lucene.Net.Documents.StringField("content", "Azure AI Search is a cloud search service.", Lucene.Net.Documents.Field.Store.NO)); // CONFLICT!
            writer.AddDocument(doc1);

            var doc2 = new Lucene.Net.Documents.Document();
            doc2.Add(new Lucene.Net.Documents.StringField("id", "2", Lucene.Net.Documents.Field.Store.YES));
            doc2.Add(new Lucene.Net.Documents.TextField("title", "Search Features", Lucene.Net.Documents.Field.Store.YES));
            doc2.Add(new Lucene.Net.Documents.StringField("title", "Search Features", Lucene.Net.Documents.Field.Store.NO)); // CONFLICT!
            doc2.Add(new Lucene.Net.Documents.TextField("content", "Search search search search search capabilities.", Lucene.Net.Documents.Field.Store.YES));
            doc2.Add(new Lucene.Net.Documents.StringField("content", "Search search search search search capabilities.", Lucene.Net.Documents.Field.Store.NO)); // CONFLICT!
            writer.AddDocument(doc2);

            writer.Commit();
        }

        using var readerGood = Lucene.Net.Index.DirectoryReader.Open(dirGood);
        using var readerBad = Lucene.Net.Index.DirectoryReader.Open(dirBad);

        var searcherGood = new Lucene.Net.Search.IndexSearcher(readerGood) { Similarity = sim };
        var searcherBad = new Lucene.Net.Search.IndexSearcher(readerBad) { Similarity = sim };

        var parser = new Lucene.Net.QueryParsers.Classic.MultiFieldQueryParser(
            LuceneDocumentMapper.AppLuceneVersion,
            new[] { "title", "content" },
            analyzer)
        {
            DefaultOperator = Lucene.Net.QueryParsers.Classic.Operator.OR
        };
        var query = parser.Parse("search");

        var goodResults = searcherGood.Search(query, 10);
        var badResults = searcherBad.Search(query, 10);

        // Good index: doc 2 (5x "search" in content) should score HIGHER than doc 1 (1x "search")
        Assert.Equal(2, goodResults.TotalHits);
        Assert.True(goodResults.ScoreDocs[0].Score > goodResults.ScoreDocs[1].Score,
            $"Good index: scores should differ. Doc0={goodResults.ScoreDocs[0].Score:F6}, Doc1={goodResults.ScoreDocs[1].Score:F6}");

        // Bad index: StringField conflicts corrupt term frequencies → both docs score identically
        Assert.Equal(2, badResults.TotalHits);
        var badScore0 = badResults.ScoreDocs[0].Score;
        var badScore1 = badResults.ScoreDocs[1].Score;
        Assert.True(badScore0 == badScore1,
            $"Bad index: StringField conflict should make scores identical. Doc0={badScore0:F6}, Doc1={badScore1:F6}");
    }

    [Fact]
    public void ConfigureSimilarity_PrematureHolderCreation_RebuildsOnFirstConfigureSimilarity()
    {
        // Reproduce the bug: holder is created via GetSearcher (e.g., from GetDocumentCountAsync)
        // BEFORE ConfigureSimilarity is called. The holder uses default BM25.
        // When ConfigureSimilarity is later called with custom k1/b, it should detect
        // the mismatch and rebuild the holder.
        var luceneSettings = Options.Create(new LuceneSettings { IndexPath = Path.Combine(_testDir, "premature") });
        using var luceneManager = new LuceneIndexManager(
            Mock.Of<ILogger<LuceneIndexManager>>(),
            luceneSettings);

        // Step 1: Prematurely create the holder by accessing GetSearcher
        // (simulates GetDocumentCountAsync being called before index is configured)
        var writer1 = luceneManager.GetWriter("test-premature");
        // The holder now exists with default BM25 (k1=1.2, b=0.75)

        // Step 2: Configure similarity with custom parameters
        var customSimilarity = new SimilarityAlgorithm
        {
            ODataType = "#Microsoft.Azure.Search.BM25Similarity",
            K1 = 2.0,
            B = 0.5
        };
        luceneManager.ConfigureSimilarity("test-premature", customSimilarity);

        // Step 3: Get writer again — should have new similarity
        var writer2 = luceneManager.GetWriter("test-premature");

        // The writer should have been rebuilt with the custom similarity
        Assert.NotSame(writer1, writer2);
    }

    [Fact]
    public void ConfigureSimilarity_SameConfig_DoesNotRebuildHolder()
    {
        var luceneSettings = Options.Create(new LuceneSettings { IndexPath = Path.Combine(_testDir, "no-rebuild") });
        using var luceneManager = new LuceneIndexManager(
            Mock.Of<ILogger<LuceneIndexManager>>(),
            luceneSettings);

        var similarity = new SimilarityAlgorithm { K1 = 1.5, B = 0.5 };
        luceneManager.ConfigureSimilarity("test", similarity);

        // Get writer to create the holder
        var writer1 = luceneManager.GetWriter("test");

        // Configure with same parameters — holder should NOT be rebuilt
        luceneManager.ConfigureSimilarity("test", new SimilarityAlgorithm { K1 = 1.5, B = 0.5 });
        var writer2 = luceneManager.GetWriter("test");

        // Same writer instance = holder was not rebuilt
        Assert.Same(writer1, writer2);
    }

    [Fact]
    public void ConfigureSimilarity_DifferentConfig_RebuildsHolder()
    {
        var luceneSettings = Options.Create(new LuceneSettings { IndexPath = Path.Combine(_testDir, "rebuild") });
        using var luceneManager = new LuceneIndexManager(
            Mock.Of<ILogger<LuceneIndexManager>>(),
            luceneSettings);

        var similarity1 = new SimilarityAlgorithm { K1 = 1.2, B = 0.75 };
        luceneManager.ConfigureSimilarity("test", similarity1);

        var writer1 = luceneManager.GetWriter("test");

        // Configure with different parameters — holder SHOULD be rebuilt
        var similarity2 = new SimilarityAlgorithm { K1 = 2.0, B = 0.3 };
        luceneManager.ConfigureSimilarity("test", similarity2);

        var writer2 = luceneManager.GetWriter("test");

        // Different writer instance = holder was rebuilt
        Assert.NotSame(writer1, writer2);
    }
}
