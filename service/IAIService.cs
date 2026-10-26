using KnowledgeBaseAPI.DTOs;

namespace KnowledgeBaseAPI.Services;

public interface IAIService
{
    bool IsConfigured { get; }

    Task<string> RewriteSearchQueryAsync(
    string question,
    IReadOnlyList<ChatHistoryMessage> history,
    CancellationToken cancellationToken);

    Task<string> GenerateAnswerAsync(
        string question,
        IReadOnlyList<ChatHistoryMessage> history,
        IReadOnlyList<RetrievedChunk> chunks,
        CancellationToken cancellationToken);
}