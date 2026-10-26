namespace KnowledgeBaseAPI.DTOs;

public sealed record ChatSourceResponse(
    int DocumentId,
    string FileName,
    int ChunkId,
    int ChunkIndex);