using KnowledgeBaseAPI.DTOs;

namespace KnowledgeBaseAPI.Services;

public interface IChatService
{
    Task<ChatAnswer> AnswerAsync(
        string question,
        IReadOnlyList<ChatHistoryMessage> history,
        CancellationToken cancellationToken);
}