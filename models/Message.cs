namespace KnowledgeBaseAPI.Models;

public class Message
{
    public int Id { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int ConversationId { get; set; }

    public Conversation? Conversation { get; set; }

    public ICollection<MessageSource> Sources { get; set; } = new List<MessageSource>();
}
