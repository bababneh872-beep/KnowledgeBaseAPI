
using System.Text.Json;
using KnowledgeBaseAPI.Data;
using KnowledgeBaseAPI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace KnowledgeBaseAPI.Services;

public sealed class QdrantVectorService
{
    private readonly QdrantClient _client;
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<QdrantVectorService> _logger;

    private string CollectionName =>
        _configuration["Qdrant:CollectionName"]
        ?? "knowledgebase_chunks";

    private int VectorSize =>
        _configuration.GetValue<int>("Qdrant:VectorSize", 1024);

    public QdrantVectorService(
        QdrantClient client,
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<QdrantVectorService> logger)
    {
        _client = client;
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task EnsureCollectionAsync(
        CancellationToken cancellationToken = default)
    {
        var collections = await _client.ListCollectionsAsync();

        if (collections.Any(c => c == CollectionName))
        {
            _logger.LogInformation(
                "Qdrant collection {CollectionName} already exists.",
                CollectionName);
            return;
        }

        await _client.CreateCollectionAsync(
            collectionName: CollectionName,
            vectorsConfig: new VectorParams
            {
                Size = (ulong)VectorSize,
                Distance = Distance.Cosine
            });

        _logger.LogInformation(
            "Created Qdrant collection {CollectionName}.",
            CollectionName);
    }

    public async Task UpsertChunkAsync(
        DocumentChunk chunk,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chunk.EmbeddingJson))
        {
            return;
        }

        var embedding =
            JsonSerializer.Deserialize<float[]>(chunk.EmbeddingJson);

        if (embedding is null || embedding.Length != VectorSize)
        {
            throw new InvalidOperationException(
                $"Chunk {chunk.Id} has an invalid embedding dimension.");
        }

        await EnsureCollectionAsync(cancellationToken);

        var point = new PointStruct
        {
            Id = (ulong)chunk.Id,
            Vectors = embedding,
            Payload =
            {
                ["document_id"] = chunk.DocumentId,
                ["file_name"] = fileName,
                ["chunk_id"] = chunk.Id,
                ["chunk_index"] = chunk.ChunkIndex,
                ["content"] = chunk.Content
            }
        };

        await _client.UpsertAsync(
            collectionName: CollectionName,
            points: new[] { point });
    }

    public async Task<int> BackfillExistingChunksAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(cancellationToken);

        var chunks = await _context.DocumentChunks
            .AsNoTracking()
            .Where(c =>
                c.EmbeddingJson != null &&
                c.Document.Status == "Completed")
            .Select(c => new
            {
                Chunk = c,
                FileName = c.Document.FileName
            })
            .ToListAsync(cancellationToken);

        var imported = 0;

        foreach (var item in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await UpsertChunkAsync(
                item.Chunk,
                item.FileName,
                cancellationToken);

            imported++;
        }

        _logger.LogInformation(
            "Backfilled {ImportedCount} chunks into Qdrant.",
            imported);

        return imported;
    }


    public async Task DeleteDocumentChunksAsync(
        int documentId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(cancellationToken);

        var filter = new Filter
        {
            Must =
        {
            new Condition
            {
                Field = new FieldCondition
                {
                    Key = "document_id",
                    Match = new Match
                    {
                        Integer = documentId
                    }
                }
            }
        }
        };

        await _client.DeleteAsync(
            collectionName: CollectionName,
            filter: filter);
    }

}
