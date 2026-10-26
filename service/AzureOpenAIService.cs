
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeBaseAPI.DTOs;

namespace KnowledgeBaseAPI.Services;

public class AzureOpenAIService : IAIService
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string _endpoint;
    private readonly string _deployment;
    private readonly string _apiVersion;

    public AzureOpenAIService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["AzureOpenAI:ApiKey"];
        _endpoint = configuration["AzureOpenAI:Endpoint"] ?? string.Empty;
        _deployment = configuration["AzureOpenAI:DeploymentName"] ?? string.Empty;
        _apiVersion = configuration["AzureOpenAI:ApiVersion"] ?? string.Empty;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_apiKey)
        && !string.IsNullOrWhiteSpace(_endpoint)
        && !string.IsNullOrWhiteSpace(_deployment)
        && !string.IsNullOrWhiteSpace(_apiVersion);



    public async Task<string> RewriteSearchQueryAsync(
        string question,
        IReadOnlyList<ChatHistoryMessage> history,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return question;
        }

        var recentHistory = history
            .TakeLast(6)
            .Select(message => $"{message.Role}: {message.Content}");

        var prompt = $"""
        Convert the latest user question into a concise search query
        for retrieving relevant passages from uploaded documents.

        Strict rules:
        - Preserve the exact meaning and intent of the latest question.
        - Identify what information the user is asking for, such as a
          person's university, degree, specialization, skills, or projects.
        - For questions asking who, what, which, where, when, or how,
          preserve the key entity and attribute being asked about.
        - For Arabic questions, generate a clear Arabic search query.
        - Preserve important Arabic words that indicate the requested
          information, such as "أي جامعة", "من أي جامعة", "ما التخصص",
          "متى", and "أين", whenever they help clarify the search intent.
        - Do not turn a specific question into vague or incomplete keywords.
        - Do not answer the question.
        - If the latest question is understandable on its own, use it as
          the main source for the search query.
        - Use conversation history only to resolve references that cannot
          be understood from the latest question alone.
        - Do not add names, dates, places, subjects, numbers, or facts
          from unrelated earlier topics.
        - Do not mix information from different people or documents.
        - Include English search terms only when they directly match the
          latest question and may help retrieve document text.
        - Return only the search query, without explanations or quotes.

        Conversation history:
        {string.Join("\n", recentHistory)}

        Latest question:
        {question}
        """;

        var messages = new object[]
        {
        new
        {
            role = "system",
            content = """
                You rewrite search queries for a document retrieval system.
                Follow the latest question and preserve its meaning.
                Never answer the question.
                Never introduce unrelated facts from conversation history.
                Never remove key information requested by the user.
                Return only the rewritten search query.
                """
        },
        new
        {
            role = "user",
            content = prompt
        }
        };

        var requestUrl =
            $"{_endpoint.TrimEnd('/')}" +
            $"/openai/deployments/{Uri.EscapeDataString(_deployment)}" +
            "/chat/completions" +
            $"?api-version={Uri.EscapeDataString(_apiVersion)}";

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            requestUrl);

        request.Headers.TryAddWithoutValidation("api-key", _apiKey);

        request.Content = JsonContent.Create(new
        {
            messages,
            max_completion_tokens = 200
        });

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return question;
        }

        await using var responseStream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        using var json = await JsonDocument.ParseAsync(
            responseStream,
            cancellationToken: cancellationToken);

        var rewrittenQuery = json.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return string.IsNullOrWhiteSpace(rewrittenQuery)
            ? question
            : rewrittenQuery.Trim();
    }


    public async Task<string> GenerateAnswerAsync(
        string question,
        IReadOnlyList<ChatHistoryMessage> history,
        IReadOnlyList<RetrievedChunk> chunks,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Azure OpenAI is not configured.");
        }

        if (chunks.Count == 0)
        {
            throw new InvalidOperationException(
                "No relevant document chunks were provided.");
        }


        const string systemPrompt = """
    You are a helpful AI assistant for a Knowledge Base.

    STRICT DOCUMENT-GROUNDED ANSWERING RULES:

    1. Answer the current user's question using only the provided
       document passages. Do not use general knowledge.

    2. Read all provided passages before deciding that information
       is missing.

    3. Search the passages for the requested fact, including relevant
       wording, synonyms, and equivalent terms.

    4. If a passage explicitly states the requested fact, answer
       directly using that fact. Do not say the information is
       unavailable when a supporting passage is present.

    5. If the passages do not support an answer, clearly say that
       the answer is not available in the uploaded documents.
       Never guess or invent facts.

    6. Cite each factual answer using the corresponding source label,
       such as [S1]. Use only labels actually provided in the passages.

    7. Treat the current question as the primary task. Use conversation
       history only to resolve references in the current question.
       Do not let unrelated earlier topics change the answer.

    8. Answer in the same language as the current question.

    9. Format answers with simple Markdown only when useful.
    """;
        var messages = new List<object>
        {
            new
            {
                role = "system",
                content = systemPrompt
            }
        };

        // Previous conversation
        messages.AddRange(
            history.Select(message => (object)new
            {
                role = message.Role,
                content = message.Content
            }));

        // Current question + relevant document passages
        var sources = chunks.Select((chunk, index) =>
            $"[S{index + 1}] {chunk.FileName}, chunk {chunk.ChunkIndex + 1}\n" +
            chunk.Content);

        var prompt =
            $"Question: {question}\n\n" +
            "Relevant document passages:\n" +
            string.Join("\n\n", sources);

        messages.Add(new
        {
            role = "user",
            content = prompt
        });

        var requestUrl =
            $"{_endpoint.TrimEnd('/')}" +
            $"/openai/deployments/{Uri.EscapeDataString(_deployment)}" +
            $"/chat/completions" +
            $"?api-version={Uri.EscapeDataString(_apiVersion)}";

        using var request =
            new HttpRequestMessage(HttpMethod.Post, requestUrl);

        request.Headers.TryAddWithoutValidation(
            "api-key",
            _apiKey);

        request.Content = JsonContent.Create(new
        {
            messages,
            max_completion_tokens = 1200
        });

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            throw new InvalidOperationException(
                $"Azure OpenAI returned {(int)response.StatusCode}: {errorBody}");
        }

        await using var responseStream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        using var json =
            await JsonDocument.ParseAsync(
                responseStream,
                cancellationToken: cancellationToken);

        var answer = json.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new InvalidOperationException(
                "Azure OpenAI returned an empty answer.");
        }

        return answer;
    }
}

