using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeBaseAPI.DTOs;
using System.Diagnostics;

namespace KnowledgeBaseAPI.Services;

public class OllamaChatService : IAIService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<OllamaChatService> _logger;

    public OllamaChatService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OllamaChatService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _model = configuration["Ollama:ChatModel"]
            ?? "qwen3:4b";
    }

    public bool IsConfigured => true;

    public Task<string> RewriteSearchQueryAsync(
        string question,
        IReadOnlyList<ChatHistoryMessage> history,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(question);
    }
    public async Task<string> GenerateAnswerAsync(
        string question,
        IReadOnlyList<ChatHistoryMessage> history,
        IReadOnlyList<RetrievedChunk> chunks,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        _logger.LogInformation(
            "Ollama chat request started. Endpoint={Endpoint}, Model={Model}, ContextChunkCount={ContextChunkCount}, HistoryCount={HistoryCount}, HttpClientTimeout={Timeout}.",
            new Uri(_httpClient.BaseAddress!, "/api/chat"),
            _model,
            chunks.Count,
            history.Count,
            _httpClient.Timeout);

        var hasDocumentContext = chunks.Count > 0;

        var systemPrompt = hasDocumentContext
            ? """
              You are a helpful AI assistant for a Knowledge Base.

              Use the provided document passages when they are relevant to the user's question.

              You may also use your general knowledge when the document passages do not fully answer the question.

              When you use information from the documents, cite the supporting passage using its [S#] label.

              Do not invent information that is not supported by the documents or your general knowledge.

              Answer naturally and clearly.
              """
            : """
              You are a helpful general-purpose AI assistant.

              Answer the user's question using your general knowledge.

              There are no relevant Knowledge Base documents for this question.

              Answer naturally, clearly, and helpfully.
              """;

        var messages = new List<object>
        {
            new
            {
                role = "system",
                content = systemPrompt
            }
        };

        messages.AddRange(
            history.Select(message => (object)new
            {
                role = message.Role,
                content = message.Content
            }));

        if (hasDocumentContext)
        {
            var sources = chunks.Select((chunk, index) =>
                $"[S{index + 1}] {chunk.FileName}, chunk {chunk.ChunkIndex + 1}\n{chunk.Content}");

            var prompt =
                $"Question: {question}\n\n" +
                "Relevant document passages:\n" +
                string.Join("\n\n", sources);

            messages.Add(new
            {
                role = "user",
                content = prompt
            });
        }
        else
        {
            messages.Add(new
            {
                role = "user",
                content = question
            });
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(
    "/api/chat",
    new
    {
        model = _model,
        messages,
        stream = false,
        think = false,
        options = new
        {
            temperature = 0.2,
            num_predict = 600
        }
    },
    cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogError(
                exception,
                "Ollama /api/chat canceled after {ElapsedMilliseconds} ms. RequestTokenCanceled={RequestTokenCanceled}, HttpClientTimeout={Timeout}.",
                timer.ElapsedMilliseconds,
                cancellationToken.IsCancellationRequested,
                _httpClient.Timeout);
            throw;
        }

        using (response)
        {
            _logger.LogInformation(
                "Ollama /api/chat response received. StatusCode={StatusCode}, ElapsedMilliseconds={ElapsedMilliseconds}.",
                (int)response.StatusCode,
                timer.ElapsedMilliseconds);
            response.EnsureSuccessStatusCode();

            await using var stream =
                await response.Content.ReadAsStreamAsync(cancellationToken);

            using var json =
                await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);

            var answer = json.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(answer))
            {
                throw new InvalidOperationException("Ollama returned an empty answer.");
            }

            _logger.LogInformation(
                "Ollama chat response parsed in {ElapsedMilliseconds} ms. AnswerLength={AnswerLength}.",
                timer.ElapsedMilliseconds,
                answer.Length);

            return answer;
        }
    }
}