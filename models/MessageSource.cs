namespace KnowledgeBaseAPI.Models;

public class MessageSource
{
    public int Id { get; set; }

    public int MessageId { get; set; }

    public int DocumentId { get; set; }

    public int ChunkId { get; set; }

    public int ChunkIndex { get; set; }

    public string FileName { get; set; } = string.Empty;

    public Message Message { get; set; } = null!;
}