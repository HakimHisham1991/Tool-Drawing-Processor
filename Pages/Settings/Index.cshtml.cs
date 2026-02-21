using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ToolDrawingProcessor.Data;
using ToolDrawingProcessor.Models;
using ToolDrawingProcessor.Services;

namespace ToolDrawingProcessor.Pages.Settings;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly AIProviderService _aiProvider;
    private readonly IConfiguration _configuration;

    public IndexModel(ApplicationDbContext db, AIProviderService aiProvider, IConfiguration configuration)
    {
        _db = db;
        _aiProvider = aiProvider;
        _configuration = configuration;
    }

    public AIProviderSettings Settings { get; set; } = new();
    public List<string> OllamaModels { get; set; } = new();
    public int TotalExtractions { get; set; }
    public int CorrectPredictions { get; set; }
    public int Corrections { get; set; }
    public int LearnedRules { get; set; }
    public List<PromptMemory> PromptRules { get; set; } = new();
    public List<CorrectionPattern> CorrectionPatterns { get; set; } = new();
    public string? Message { get; set; }
    public bool IsError { get; set; }

    public class CorrectionPattern
    {
        public string Field { get; set; } = "";
        public int Total { get; set; }
        public int Correct { get; set; }
        public int Incorrect { get; set; }
    }

    public async Task OnGetAsync()
    {
        await LoadDataAsync();
    }

    public async Task<IActionResult> OnPostSaveSettingsAsync(
        string provider, string ollamaEndpoint, string model,
        int timeout, string? openaiKey, string? anthropicKey)
    {
        try
        {
            // Update in-memory configuration
            _configuration["AIProvider:Provider"] = provider;
            _configuration["AIProvider:OllamaEndpoint"] = ollamaEndpoint;
            _configuration["AIProvider:Model"] = model;
            _configuration["AIProvider:TimeoutSeconds"] = timeout.ToString();
            _configuration["AIProvider:OpenAIApiKey"] = openaiKey ?? "";
            _configuration["AIProvider:AnthropicApiKey"] = anthropicKey ?? "";

            Message = "Settings saved successfully.";
            IsError = false;
        }
        catch (Exception ex)
        {
            Message = $"Error saving settings: {ex.Message}";
            IsError = true;
        }

        await LoadDataAsync();
        return Page();
    }

    private async Task LoadDataAsync()
    {
        Settings = _aiProvider.GetSettings();
        OllamaModels = await _aiProvider.GetOllamaModelsAsync();

        TotalExtractions = await _db.CorrectionFeedbacks.CountAsync();
        CorrectPredictions = await _db.CorrectionFeedbacks.CountAsync(f => f.WasCorrect);
        Corrections = await _db.CorrectionFeedbacks.CountAsync(f => !f.WasCorrect);
        LearnedRules = await _db.PromptMemories.CountAsync();

        PromptRules = await _db.PromptMemories
            .OrderByDescending(r => r.UsageCount)
            .Take(10)
            .ToListAsync();

        CorrectionPatterns = await _db.CorrectionFeedbacks
            .GroupBy(c => c.FieldName)
            .Select(g => new CorrectionPattern
            {
                Field = g.Key,
                Total = g.Count(),
                Correct = g.Count(x => x.WasCorrect),
                Incorrect = g.Count(x => !x.WasCorrect)
            })
            .OrderByDescending(p => p.Incorrect)
            .ToListAsync();
    }
}
