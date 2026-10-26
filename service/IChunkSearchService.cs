using KnowledgeBaseAPI.DTOs;

namespace KnowledgeBaseAPI.Services;

public interface IChunkSearchService
{
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string question, CancellationToken cancellationToken);
}