using Microsoft.EntityFrameworkCore;
using ToolDrawingProcessor.Data;
using ToolDrawingProcessor.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to use port 5500
builder.WebHost.UseUrls("http://localhost:5500");

// Add services to the container
builder.Services.AddRazorPages();

// Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=toolprocessor.db"));

// HTTP clients for AI providers
builder.Services.AddHttpClient("Ollama", client =>
{
    client.Timeout = TimeSpan.FromSeconds(120);
});
builder.Services.AddHttpClient("OpenAI", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddHttpClient("Anthropic", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

// Application services
builder.Services.AddSingleton<PdfTextExtractorService>();
builder.Services.AddSingleton<LearningEngineService>();
builder.Services.AddSingleton<AIProviderService>();
builder.Services.AddSingleton<ProcessingService>();
builder.Services.AddSingleton<ExcelExportService>();

// Logging
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Ensure uploads directory exists
var uploadsPath = Path.Combine(app.Environment.WebRootPath, "uploads");
Directory.CreateDirectory(uploadsPath);

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

// Minimal API endpoint for Ollama status check
app.MapGet("/api/ollama-status", async (AIProviderService aiProvider) =>
{
    var online = await aiProvider.IsOllamaOnlineAsync();
    return Results.Json(new { online });
});

app.Run();
