using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgeBaseAPI.Services;

public class OllamaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<OllamaEmbeddingService> _logger;

    public OllamaEmbeddingService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OllamaEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _model = configuration["Ollama:EmbeddingModel"]
            ?? "qwen3-embedding:0.6b";
    }

    public async Task<float[]> CreateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        _logger.LogInformation(
            "Ollama embedding request started. Endpoint={Endpoint}, Model={Model}, TextLength={TextLength}, HttpClientTimeout={Timeout}.",
            new Uri(_httpClient.BaseAddress!, "/api/embed"),
            _model,
            text.Length,
            _httpClient.Timeout);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(
                "/api/embed",
                new
                {
                    model = _model,
                    input = text
                },
                cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogError(
                exception,
                "Ollama /api/embed canceled after {ElapsedMilliseconds} ms. RequestTokenCanceled={RequestTokenCanceled}, HttpClientTimeout={Timeout}.",
                timer.ElapsedMilliseconds,
                cancellationToken.IsCancellationRequested,
                _httpClient.Timeout);
            throw;
        }

        using (response)
        {
            _logger.LogInformation(
                "Ollama /api/embed response received. StatusCode={StatusCode}, ElapsedMilliseconds={ElapsedMilliseconds}.",
                (int)response.StatusCode,
                timer.ElapsedMilliseconds);
            response.EnsureSuccessStatusCode();

            await using var stream =
                await response.Content.ReadAsStreamAsync(cancellationToken);

            using var json =
                await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);

            var embeddings =
                json.RootElement.GetProperty("embeddings");

            if (embeddings.GetArrayLength() == 0)
            {
                throw new InvalidOperationException(
                    "Ollama returned no embedding.");
            }

            var result = embeddings[0]
                .EnumerateArray()
                .Select(item => item.GetSingle())
                .ToArray();

            _logger.LogInformation(
                "Ollama embedding completed in {ElapsedMilliseconds} ms with {Dimensions} dimensions.",
                timer.ElapsedMilliseconds,
                result.Length);

            return result;
        }
    }
}