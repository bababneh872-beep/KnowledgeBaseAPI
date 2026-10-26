namespace KnowledgeBaseAPI.DTOs;

public sealed record RetrievedChunk(
    int DocumentId,
    string FileName,
    int ChunkId,
    int ChunkIndex,
    string Content,
    int Score);