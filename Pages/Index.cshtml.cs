using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ToolDrawingProcessor.Data;
using ToolDrawingProcessor.Models;

namespace ToolDrawingProcessor.Pages;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public int PendingCount { get; set; }
    public int ProcessingCount { get; set; }
    public int ProcessedCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalCorrections { get; set; }
    public double AccuracyRate { get; set; }
    public int LearnedRules { get; set; }
    public int TotalProcessed { get; set; }
    public List<UploadedFile> RecentFiles { get; set; } = new();

    public async Task OnGetAsync()
    {
        PendingCount = await _db.UploadedFiles.CountAsync(f => f.Status == FileStatus.Pending);
        ProcessingCount = await _db.UploadedFiles.CountAsync(f => f.Status == FileStatus.Processing);
        ProcessedCount = await _db.UploadedFiles.CountAsync(f => f.Status == FileStatus.Processed);
        FailedCount = await _db.UploadedFiles.CountAsync(f => f.Status == FileStatus.Failed);

        var totalFeedback = await _db.CorrectionFeedbacks.CountAsync();
        var correctFeedback = await _db.CorrectionFeedbacks.CountAsync(f => f.WasCorrect);
        TotalCorrections = await _db.CorrectionFeedbacks.CountAsync(f => !f.WasCorrect);
        AccuracyRate = totalFeedback > 0 ? (double)correctFeedback / totalFeedback : 0;

        LearnedRules = await _db.PromptMemories.CountAsync();
        TotalProcessed = ProcessedCount;

        RecentFiles = await _db.UploadedFiles
            .OrderByDescending(f => f.UploadedAt)
            .Take(10)
            .ToListAsync();
    }
}
