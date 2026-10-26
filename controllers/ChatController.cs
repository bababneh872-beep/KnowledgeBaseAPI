using KnowledgeBaseAPI.Data;
using KnowledgeBaseAPI.DTOs;
using KnowledgeBaseAPI.Models;
using KnowledgeBaseAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBaseAPI.Controllers;


[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IChatService _chatService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        ApplicationDbContext context,
        IChatService chatService,
        ILogger<ChatController> logger)
    {
        _context = context;
        _chatService = chatService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Ask(ChatRequest request, CancellationToken cancellationToken)
    {
        var question = request.Question.Trim();
        if (question.Length < 2)
        {
            return BadRequest(new { message = "Enter a question with at least two characters." });
        }

        var conversation = request.ConversationId is int conversationId
            ? await _context.Conversations.FirstOrDefaultAsync(item => item.Id == conversationId, cancellationToken)
            : null;

        if (request.ConversationId.HasValue && conversation == null)
        {
            return NotFound(new { message = "Conversation not found." });
        }

        var history = Array.Empty<ChatHistoryMessage>();
        if (conversation != null)
        {
            var previousMessages = await _context.Messages
                .AsNoTracking()
                .Where(message => message.ConversationId == conversation.Id
                    && (message.Role == "user" || message.Role == "assistant"))
                .OrderByDescending(message => message.CreatedAt)
                .Take(12)
                .Select(message => new ChatHistoryMessage(message.Role, message.Content))
                .ToListAsync(cancellationToken);
            previousMessages.Reverse();
            history = previousMessages.ToArray();
        }

        _logger.LogInformation(
            "Chat request entered. TraceIdentifier={TraceIdentifier}, ConversationId={ConversationId}, HistoryCount={HistoryCount}, RequestAborted={RequestAborted}.",
            HttpContext.TraceIdentifier,
            conversation?.Id,
            history.Length,
            cancellationToken.IsCancellationRequested);

        ChatAnswer answer;
        try
        {
            _logger.LogInformation(
    "Before ChatService. RequestAborted={RequestAborted}.",
    HttpContext.RequestAborted.IsCancellationRequested);
            answer = await _chatService.AnswerAsync(question, history, cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogError(
                exception,
                "Chat request canceled in service pipeline. TraceIdentifier={TraceIdentifier}, RequestAborted={RequestAborted}.",
                HttpContext.TraceIdentifier,
                cancellationToken.IsCancellationRequested);
            throw;
        }

        _logger.LogInformation(
            "Chat service returned. TraceIdentifier={TraceIdentifier}, Mode={Mode}, SourceCount={SourceCount}.",
            HttpContext.TraceIdentifier,
            answer.Mode,
            answer.Sources.Count);
        conversation ??= new Conversation();
        var userMessage = new Message
        {
            Role = "user",
            Content = question,
            CreatedAt = DateTime.UtcNow,
            Conversation = conversation
        };
        var assistantMessage = new Message
        {
            Role = "assistant",
            Content = answer.Answer,
            CreatedAt = DateTime.UtcNow,
            Conversation = conversation,
            Sources = answer.Sources.Select(source => new MessageSource
            {
                DocumentId = source.DocumentId,
                ChunkId = source.ChunkId,
                ChunkIndex = source.ChunkIndex,
                FileName = source.FileName
            }).ToList()
        };

        _context.Messages.AddRange(userMessage, assistantMessage);
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            conversationId = conversation.Id,
            answer = answer.Answer,
            sources = answer.Sources.Select(source => new
            {
                fileName = source.FileName,
                chunkIndex = source.ChunkIndex
            })
        });
    }

    [HttpDelete("{conversationId:int}")]
    public async Task<IActionResult> DeleteConversation(
        int conversationId,
        CancellationToken cancellationToken)
    {
        var conversation = await _context.Conversations
            .Include(c => c.Messages)
                .ThenInclude(m => m.Sources)
            .FirstOrDefaultAsync(
                c => c.Id == conversationId,
                cancellationToken);

        if (conversation == null)
        {
            return NotFound(new
            {
                message = "Conversation not found."
            });
        }

        _context.Conversations.Remove(conversation);

        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

   
}