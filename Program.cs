
using KnowledgeBaseAPI.Data;
using KnowledgeBaseAPI.Services;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<PdfService>();
builder.Services.AddScoped<OcrService>();

builder.Services.AddHttpClient<OllamaVisionService>(client =>
{
    var baseUrl = builder.Configuration["Ollama:BaseUrl"]
        ?? "http://localhost:11434";

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddScoped<TextChunkingService>();
builder.Services.AddScoped<IChunkSearchService, ChunkSearchService>();
builder.Services.AddScoped<IChatService, ChatService>();

builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var host = configuration["Qdrant:Host"] ?? "localhost";
    var port = configuration.GetValue<int>("Qdrant:Port", 6334);

    return new Qdrant.Client.QdrantClient(host, port);
});

builder.Services.AddScoped<QdrantVectorService>();


builder.Services.AddHttpClient<IAIService, AzureOpenAIService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>(
    (serviceProvider, client) =>
    {
        var configuration =
            serviceProvider.GetRequiredService<IConfiguration>();

        var baseUrl = configuration["Ollama:BaseUrl"]
            ?? "http://localhost:11434";

        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromMinutes(5);
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<ApplicationDbContext>();

    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowAngular");

app.MapControllers();

app.Run();

