namespace KnowledgeBaseAPI.DTOs;

public sealed record ChatAnswer(
    string Answer,
    string Mode,
    IReadOnlyList<ChatSourceResponse> Sources);