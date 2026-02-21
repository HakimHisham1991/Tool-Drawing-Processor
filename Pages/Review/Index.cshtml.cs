using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ToolDrawingProcessor.Data;
using ToolDrawingProcessor.Models;
using ToolDrawingProcessor.Services;

namespace ToolDrawingProcessor.Pages.Review;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly LearningEngineService _learningEngine;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ApplicationDbContext db, LearningEngineService learningEngine, ILogger<IndexModel> logger)
    {
        _db = db;
        _learningEngine = learningEngine;
        _logger = logger;
    }

    public new UploadedFile? File { get; set; }
    public ToolSpecification? Spec { get; set; }
    public AIExtractionResult? AIResult { get; set; }
    public string? Message { get; set; }
    public bool IsError { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await LoadDataAsync(id);

        if (File == null)
        {
            return RedirectToPage("/Processing/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAcceptAsync(
        int fileId,
        string? toolType,
        double? diameter,
        double? fluteLength,
        double? cornerRadius,
        double? shankDiameter,
        double? totalLength,
        int? numberOfFlutes)
    {
        await LoadDataAsync(fileId);

        if (File == null || Spec == null)
        {
            return RedirectToPage("/Processing/Index");
        }

        try
        {
            // Build the AI result for comparison
            var originalAIResult = AIResult ?? new AIExtractionResult();

            // Update spec with user-accepted values
            Spec.ToolType = toolType;
            Spec.Diameter = diameter;
            Spec.FluteLength = fluteLength;
            Spec.CornerRadius = cornerRadius;
            Spec.ShankDiameter = shankDiameter;
            Spec.TotalLength = totalLength;
            Spec.NumberOfFlutes = numberOfFlutes;

            // Record corrections and calculate correction count
            await _learningEngine.RecordCorrectionsAsync(fileId, originalAIResult, Spec);

            // Count corrections for this file
            var correctionCount = await _db.CorrectionFeedbacks
                .CountAsync(c => c.UploadedFileId == fileId && !c.WasCorrect);
            Spec.CorrectionCount = correctionCount;

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Accepted specifications for file {FileId} with {Corrections} corrections",
                fileId, correctionCount);

            Message = $"Specifications accepted and saved. {correctionCount} correction(s) recorded for AI training.";
            IsError = false;

            // Reload data to reflect changes
            await LoadDataAsync(fileId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting specifications for file {FileId}", fileId);
            Message = $"Error saving: {ex.Message}";
            IsError = true;
        }

        return Page();
    }

    private async Task LoadDataAsync(int fileId)
    {
        File = await _db.UploadedFiles
            .Include(f => f.ToolSpecification)
            .Include(f => f.AIExtractionLog)
            .FirstOrDefaultAsync(f => f.Id == fileId);

        if (File != null)
        {
            Spec = File.ToolSpecification;

            // Parse AI extraction result from log
            if (File.AIExtractionLog != null && !string.IsNullOrEmpty(File.AIExtractionLog.RawAIResponseJson))
            {
                try
                {
                    var jsonMatch = System.Text.RegularExpressions.Regex.Match(
                        File.AIExtractionLog.RawAIResponseJson,
                        @"\{[\s\S]*\}",
                        System.Text.RegularExpressions.RegexOptions.Multiline);

                    if (jsonMatch.Success)
                    {
                        AIResult = JsonSerializer.Deserialize<AIExtractionResult>(
                            jsonMatch.Value,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not parse AI result for review display");
                }
            }
        }
    }
}
