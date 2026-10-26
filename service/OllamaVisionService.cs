
using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgeBaseAPI.Services;

public class OllamaVisionService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public OllamaVisionService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<string> ExtractTextFromImageAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException(
                "Image file not found.",
                imagePath);
        }

        var model = _configuration["Ollama:VisionModel"]
            ?? "qwen3-vl:2b-instruct";

        var imageBytes = await File.ReadAllBytesAsync(
            imagePath,
            cancellationToken);

        var imageBase64 = Convert.ToBase64String(imageBytes);

        var request = new
        {
            model,
            stream = false,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content =
                        "Extract all readable text from this image. " +
                        "Preserve Arabic and English text, numbers, " +
                        "headings, and line breaks. Do not summarize " +
                        "or translate. Return only the extracted text.",
                    images = new[] { imageBase64 }
                }
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(
            "/api/chat",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(
                cancellationToken),
            cancellationToken: cancellationToken);

        var extractedText = json.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString()
            ?.Trim();

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            throw new InvalidOperationException(
                "The vision model returned no readable text.");
        }

        return extractedText;
    }
}
