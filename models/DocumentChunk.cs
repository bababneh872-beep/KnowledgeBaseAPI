namespace KnowledgeBaseAPI.Models
{
    public class DocumentChunk
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public int ChunkIndex { get; set; }
        public string Content { get; set; } = string.Empty;

        // Embedding vector stored as JSON.
        public string? EmbeddingJson { get; set; }

        public Document Document { get; set; } = null!;
    }
}