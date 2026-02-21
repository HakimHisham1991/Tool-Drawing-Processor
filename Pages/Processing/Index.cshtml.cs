using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ToolDrawingProcessor.Data;
using ToolDrawingProcessor.Models;
using ToolDrawingProcessor.Services;

namespace ToolDrawingProcessor.Pages.Processing;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ProcessingService _processingService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ApplicationDbContext db, ProcessingService processingService, ILogger<IndexModel> logger)
    {
        _db = db;
        _processingService = processingService;
        _logger = logger;
    }

    public List<UploadedFile> Files { get; set; } = new();
    public int PendingCount { get; set; }
    public int ProcessingCount { get; set; }
    public int ProcessedCount { get; set; }
    public int FailedCount { get; set; }
    public string? Message { get; set; }
    public bool IsError { get; set; }

    public async Task OnGetAsync()
    {
        await LoadDataAsync();
    }

    public async Task<IActionResult> OnPostProcessNextAsync()
    {
        var (processed, fileId, error) = await _processingService.ProcessNextAsync();

        if (processed)
        {
            Message = $"Successfully processed file (ID: {fileId}). Ready for review.";
            IsError = false;
        }
        else
        {
            Message = error ?? "No files to process.";
            IsError = true;
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostProcessAllAsync()
    {
        int processedCount = 0;
        var errors = new List<string>();

        // Process sequentially
        while (true)
        {
            var (processed, fileId, error) = await _processingService.ProcessNextAsync();
            if (!processed)
            {
                if (error != null && !error.Contains("No pending"))
                    errors.Add(error);
                break;
            }
            processedCount++;
        }

        if (processedCount > 0)
        {
            Message = $"Successfully processed {processedCount} file(s).";
            if (errors.Any())
                Message += $" Errors: {string.Join("; ", errors)}";
            IsError = errors.Any();
        }
        else
        {
            Message = errors.Any() ? string.Join("; ", errors) : "No pending files to process.";
            IsError = true;
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostProcessSingleAsync(int fileId)
    {
        // Reset status to Pending first so it can be re-processed
        var file = await _db.UploadedFiles.FindAsync(fileId);
        if (file != null)
        {
            file.Status = FileStatus.Pending;
            file.ErrorMessage = null;

            // Remove old extraction data
            var oldLog = await _db.AIExtractionLogs.FirstOrDefaultAsync(l => l.UploadedFileId == fileId);
            if (oldLog != null) _db.AIExtractionLogs.Remove(oldLog);

            var oldSpec = await _db.ToolSpecifications.FirstOrDefaultAsync(s => s.UploadedFileId == fileId);
            if (oldSpec != null) _db.ToolSpecifications.Remove(oldSpec);

            await _db.SaveChangesAsync();
        }

        var (processed, _, error) = await _processingService.ProcessFileByIdAsync(fileId);

        if (processed)
        {
            Message = $"Successfully processed file (ID: {fileId}).";
            IsError = false;
        }
        else
        {
            Message = error ?? "Processing failed.";
            IsError = true;
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostClearQueueAsync()
    {
        var files = await _db.UploadedFiles.ToListAsync();

        foreach (var file in files)
        {
            var log = await _db.AIExtractionLogs.FirstOrDefaultAsync(l => l.UploadedFileId == file.Id);
            if (log != null) _db.AIExtractionLogs.Remove(log);

            var spec = await _db.ToolSpecifications.FirstOrDefaultAsync(s => s.UploadedFileId == file.Id);
            if (spec != null) _db.ToolSpecifications.Remove(spec);

            var feedbacks = _db.CorrectionFeedbacks.Where(f => f.UploadedFileId == file.Id);
            _db.CorrectionFeedbacks.RemoveRange(feedbacks);

            if (File.Exists(file.FilePath))
            {
                try { File.Delete(file.FilePath); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not delete file: {Path}", file.FilePath); }
            }
        }

        _db.UploadedFiles.RemoveRange(files);
        await _db.SaveChangesAsync();

        Message = $"Cleared {files.Count} file(s) from the queue.";
        IsError = false;

        await LoadDataAsync();
        return Page();
    }

    private async Task LoadDataAsync()
    {
        Files = await _db.UploadedFiles
            .OrderByDescending(f => f.UploadedAt)
            .ToListAsync();

        var (pending, processing, processed, failed) = await _processingService.GetQueueStatusAsync();
        PendingCount = pending;
        ProcessingCount = processing;
        ProcessedCount = processed;
        FailedCount = failed;
    }
}
