
using System.Diagnostics;
using KnowledgeBaseAPI.DTOs;

namespace KnowledgeBaseAPI.Services;

public class ChatService : IChatService
{
    private readonly IChunkSearchService _chunkSearch;
    private readonly IAIService _aiService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IChunkSearchService chunkSearch,
        IAIService aiService,
        IConfiguration configuration,
        ILogger<ChatService> logger)
    {
        _chunkSearch = chunkSearch;
        _aiService = aiService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ChatAnswer> AnswerAsync(
        string question,
        IReadOnlyList<ChatHistoryMessage> history,
        CancellationToken cancellationToken)
    {
        var retrievalTimer = Stopwatch.StartNew();

        _logger.LogInformation(
            "Document retrieval started. QuestionLength={QuestionLength}, HistoryCount={HistoryCount}.",
            question.Length,
            history.Count);


        IReadOnlyList<RetrievedChunk> allChunks;

        var searchQuery = question;

        try
        {
            searchQuery = await _aiService.RewriteSearchQueryAsync(
                question,
                history,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                searchQuery = question;
            }

            _logger.LogInformation(
                "Search query rewritten. OriginalQuestion={OriginalQuestion}, SearchQuery={SearchQuery}",
                question,
                searchQuery);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Search query rewriting failed. Falling back to original question.");

            searchQuery = question;
        }

        
try
        {
            allChunks = await _chunkSearch.SearchAsync(
                searchQuery,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogError(
                exception,
                "Document retrieval canceled after {ElapsedMilliseconds} ms. RequestTokenCanceled={RequestTokenCanceled}.",
                retrievalTimer.ElapsedMilliseconds,
                cancellationToken.IsCancellationRequested);

            throw;
        }
        

        _logger.LogInformation(
            "Document retrieval completed after {ElapsedMilliseconds} ms. RetrievedCount={RetrievedCount}.",
            retrievalTimer.ElapsedMilliseconds,
            allChunks.Count);

        var threshold =
    _configuration.GetValue<double>(
        "Rag:SimilarityThreshold",
        0.23);
        var relevantChunks = allChunks
            .Where(chunk =>
                chunk.Score >= threshold * 1_000_000)
            .ToArray();
        _logger.LogInformation(
    "Relevant chunks details: {Chunks}",
    string.Join(" || ", relevantChunks.Select(chunk =>
        $"File={chunk.FileName}, " +
        $"Chunk={chunk.ChunkIndex}, " +
        $"Score={chunk.Score / 1_000_000.0:F4}, " +
        $"Content={chunk.Content}")));

        // No relevant information was found in the uploaded documents.
        if (relevantChunks.Length == 0)
        {
            _logger.LogInformation(
                "No relevant document chunks found. Returning no-context answer.");

            return new ChatAnswer(
                "عذرًا، لا أعرف الإجابة من الملفات المرفوعة.",
                "no-context",
                Array.Empty<ChatSourceResponse>());
        }
        _logger.LogInformation(
    "RAG diagnostics: Threshold={Threshold}, Retrieved={RetrievedCount}, Relevant={RelevantCount}, TopScores={TopScores}",
    threshold,
    allChunks.Count,
    relevantChunks.Length,
    string.Join(", ", allChunks
        .Take(20)
        .Select(chunk => chunk.Score)));

        _logger.LogInformation(
            "Relevant document chunks found. Threshold={Threshold}, RelevantChunkCount={RelevantChunkCount}.",
            threshold,
            relevantChunks.Length);

        var sources = relevantChunks
            .Select(chunk => new ChatSourceResponse(
                chunk.DocumentId,
                chunk.FileName,
                chunk.ChunkId,
                chunk.ChunkIndex))
            .ToArray();

        try
        {
            var chatTimer = Stopwatch.StartNew();

            _logger.LogInformation(
                "Document-based answer generation started. Service={Service}, ContextChunkCount={ContextChunkCount}, HistoryCount={HistoryCount}.",
                _aiService.GetType().Name,
                relevantChunks.Length,
                history.Count);

            var answer =
                await _aiService.GenerateAnswerAsync(
                    question,
                    history,
                    relevantChunks,
                    cancellationToken);

            _logger.LogInformation(
                "Document-based answer generation completed after {ElapsedMilliseconds} ms.",
                chatTimer.ElapsedMilliseconds);

            return new ChatAnswer(
                answer,
                "ollama-rag",
                sources);
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogError(
                exception,
                "Document-based answer generation canceled. RequestTokenCanceled={RequestTokenCanceled}.",
                cancellationToken.IsCancellationRequested);

            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Document-based AI request failed.");

            throw;
        }
    }
}
