using System.ComponentModel.DataAnnotations;

namespace KnowledgeBaseAPI.DTOs;

public sealed class ChatRequest
{
    [Required]
    [StringLength(2000, MinimumLength = 2)]
    public string Question { get; set; } = string.Empty;

    public int? ConversationId { get; set; }
}