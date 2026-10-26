
using System.Diagnostics;
using KnowledgeBaseAPI.DTOs;
using Qdrant.Client;

namespace KnowledgeBaseAPI.Services;

public class ChunkSearchService : IChunkSearchService
{
    private readonly QdrantClient _qdrantClient;
    private readonly IEmbeddingService _embeddingService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChunkSearchService> _logger;

    private string CollectionName =>
        _configuration["Qdrant:CollectionName"]
        ?? "knowledgebase_chunks";

    public ChunkSearchService(
        QdrantClient qdrantClient,
        IEmbeddingService embeddingService,
        IConfiguration configuration,
        ILogger<ChunkSearchService> logger)
    {
        _qdrantClient = qdrantClient;
        _embeddingService = embeddingService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string question,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return Array.Empty<RetrievedChunk>();
        }

        var timer = Stopwatch.StartNew();

        using var timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
        var token = timeoutCts.Token;

        try
        {
            _logger.LogInformation(
                "Qdrant chunk search started. QuestionLength={Length}",
                question.Length);

            // 1. إنشاء embedding للسؤال باستخدام Ollama.
            var embedding =
                await _embeddingService.CreateEmbeddingAsync(
                    question,
                    token);

            if (embedding == null || embedding.Length == 0)
            {
                _logger.LogWarning(
                    "Question embedding was empty.");

                return Array.Empty<RetrievedChunk>();
            }

            var expectedDimensions =
                _configuration.GetValue<int>(
                    "Qdrant:VectorSize", 1024);

            if (embedding.Length != expectedDimensions)
            {
                _logger.LogError(
                    "Embedding dimension mismatch. Expected={Expected}, Actual={Actual}",
                    expectedDimensions,
                    embedding.Length);

                return Array.Empty<RetrievedChunk>();
            }

            // 2. البحث داخل Qdrant بدل تحميل جميع embeddings من SQL.
            var points = await _qdrantClient.SearchAsync(
    collectionName: CollectionName,
    vector: embedding,
    limit: 20,
    payloadSelector: true,
    cancellationToken: token);

            // 3. تحويل نتائج Qdrant إلى DTO المستخدم حاليًا بالمشروع.

            _logger.LogInformation(
                "Qdrant returned {PointCount} points.",
                points.Count);

            foreach (var point in points.Take(3))
            {
                _logger.LogInformation(
                    "Qdrant point {PointId}, Score={Score}, PayloadKeys={PayloadKeys}",
                    point.Id,
                    point.Score,
                    string.Join(", ", point.Payload.Keys));
            }

            var results = points
                .Select(point =>
                {
                    var payload = point.Payload;

                    if (!payload.TryGetValue("document_id", out var documentIdValue) ||
                        !payload.TryGetValue("chunk_id", out var chunkIdValue) ||
                        !payload.TryGetValue("chunk_index", out var chunkIndexValue) ||
                        !payload.TryGetValue("content", out var contentValue))
                    {
                        return null;
                    }

                    string fileName;

                    if (payload.TryGetValue("file_name", out var fileNameValue))
                    {
                        fileName = fileNameValue.StringValue;
                    }
                    else if (payload.TryGetValue("title_name", out var titleNameValue))
                    {
                        fileName = titleNameValue.StringValue;
                    }
                    else
                    {
                        fileName = "Unknown";
                    }

                    var content = contentValue.StringValue;

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        return null;
                    }

                    return new RetrievedChunk(
                        DocumentId: checked((int)documentIdValue.IntegerValue),
                        FileName: fileName,
                        ChunkId: checked((int)chunkIdValue.IntegerValue),
                        ChunkIndex: checked((int)chunkIndexValue.IntegerValue),
                        Content: content,
                        Score: (int)Math.Round(point.Score * 1_000_000));
                })
                .Where(chunk => chunk is not null)
                .Select(chunk => chunk!)
                .OrderByDescending(chunk => chunk.Score)
                .Take(20)
                .ToArray();


            _logger.LogInformation(
                "Qdrant search completed in {Elapsed} ms. " +
                "PointsFound={PointsFound}, Results={ResultCount}, " +
                "TopScore={TopScore}",
                timer.ElapsedMilliseconds,
                points.Count,
                results.Length,
                results.FirstOrDefault()?.Score / 1_000_000.0);

            _logger.LogInformation(
                "Top Qdrant scores: {Scores}",
                string.Join(
                    " | ",
                    results.Take(5).Select(chunk =>
                        $"Score={chunk.Score / 1_000_000.0:F4}, " +
                        $"File={chunk.FileName}, " +
                        $"Chunk={chunk.ChunkIndex}")));

            return results;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Qdrant search canceled by the request.");

            throw;
        }
        catch (OperationCanceledException)
            when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Qdrant search timed out after {Elapsed} ms.",
                timer.ElapsedMilliseconds);

            return Array.Empty<RetrievedChunk>();
        }
    }
}
