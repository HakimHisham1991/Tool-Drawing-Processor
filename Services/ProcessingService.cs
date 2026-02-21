using Microsoft.EntityFrameworkCore;
using ToolDrawingProcessor.Data;
using ToolDrawingProcessor.Models;

namespace ToolDrawingProcessor.Services;

/// <summary>
/// Orchestrates sequential processing of uploaded PDF files.
/// </summary>
public class ProcessingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PdfTextExtractorService _pdfExtractor;
    private readonly AIProviderService _aiProvider;
    private readonly ILogger<ProcessingService> _logger;

    private static readonly SemaphoreSlim _processingLock = new(1, 1);

    public ProcessingService(
        IServiceScopeFactory scopeFactory,
        PdfTextExtractorService pdfExtractor,
        AIProviderService aiProvider,
        ILogger<ProcessingService> logger)
    {
        _scopeFactory = scopeFactory;
        _pdfExtractor = pdfExtractor;
        _aiProvider = aiProvider;
        _logger = logger;
    }

    /// <summary>
    /// Process the next pending file sequentially.
    /// </summary>
    public async Task<(bool processed, int? fileId, string? error)> ProcessNextAsync()
    {
        if (!await _processingLock.WaitAsync(0))
        {
            return (false, null, "Processing is already in progress. Please wait.");
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var file = await db.UploadedFiles
                .Where(f => f.Status == FileStatus.Pending)
                .OrderBy(f => f.UploadedAt)
                .FirstOrDefaultAsync();

            if (file == null)
            {
                return (false, null, "No pending files to process.");
            }

            return await ProcessFileAsync(db, file);
        }
        finally
        {
            _processingLock.Release();
        }
    }

    /// <summary>
    /// Process a specific file by ID.
    /// </summary>
    public async Task<(bool processed, int? fileId, string? error)> ProcessFileByIdAsync(int fileId)
    {
        if (!await _processingLock.WaitAsync(0))
        {
            return (false, fileId, "Processing is already in progress. Please wait.");
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var file = await db.UploadedFiles.FindAsync(fileId);
            if (file == null)
            {
                return (false, fileId, "File not found.");
            }

            return await ProcessFileAsync(db, file);
        }
        finally
        {
            _processingLock.Release();
        }
    }

    private async Task<(bool processed, int? fileId, string? error)> ProcessFileAsync(
        ApplicationDbContext db, UploadedFile file)
    {
        try
        {
            file.Status = FileStatus.Processing;
            await db.SaveChangesAsync();

            _logger.LogInformation("Processing file: {FileName} (ID: {Id})", file.FileName, file.Id);

            // Step 1: Extract text from PDF
            var pdfText = await _pdfExtractor.ExtractTextAsync(file.FilePath);

            // Step 2: Send to AI for extraction
            var (result, rawJson, provider, model) = await _aiProvider.ExtractSpecificationsAsync(pdfText);

            // Step 3: Store AI extraction log
            var aiLog = new AIExtractionLog
            {
                UploadedFileId = file.Id,
                RawAIResponseJson = rawJson,
                ExtractedAt = DateTime.UtcNow
            };
            db.AIExtractionLogs.Add(aiLog);

            // Step 4: Create preliminary tool specification (user will review)
            var overallConfidence = CalculateOverallConfidence(result);

            var spec = new ToolSpecification
            {
                UploadedFileId = file.Id,
                SourceFileName = file.FileName,
                ToolType = result.ToolType.Value,
                Diameter = result.Diameter.Value,
                FluteLength = result.FluteLength.Value,
                CornerRadius = result.CornerRadius.Value,
                ShankDiameter = result.ShankDiameter.Value,
                TotalLength = result.TotalLength.Value,
                NumberOfFlutes = result.NumberOfFlutes.Value,
                OverallConfidence = overallConfidence,
                AIProviderUsed = provider,
                ModelUsed = model,
                CreatedAt = DateTime.UtcNow
            };
            db.ToolSpecifications.Add(spec);

            file.Status = FileStatus.Processed;
            file.ErrorMessage = null;
            await db.SaveChangesAsync();

            _logger.LogInformation(
                "Successfully processed file: {FileName} with {Confidence:P0} confidence",
                file.FileName, overallConfidence);

            return (true, file.Id, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process file: {FileName}", file.FileName);

            file.Status = FileStatus.Failed;
            file.ErrorMessage = ex.Message;
            await db.SaveChangesAsync();

            return (false, file.Id, ex.Message);
        }
    }

    private static double CalculateOverallConfidence(AIExtractionResult result)
    {
        var confidences = new[]
        {
            result.ToolType.Confidence,
            result.Diameter.Confidence,
            result.FluteLength.Confidence,
            result.CornerRadius.Confidence,
            result.ShankDiameter.Confidence,
            result.TotalLength.Confidence,
            result.NumberOfFlutes.Confidence
        };

        return confidences.Average();
    }

    /// <summary>
    /// Get processing queue status.
    /// </summary>
    public async Task<(int pending, int processing, int processed, int failed)> GetQueueStatusAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var counts = await db.UploadedFiles
            .GroupBy(f => f.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        return (
            counts.FirstOrDefault(c => c.Status == FileStatus.Pending)?.Count ?? 0,
            counts.FirstOrDefault(c => c.Status == FileStatus.Processing)?.Count ?? 0,
            counts.FirstOrDefault(c => c.Status == FileStatus.Processed)?.Count ?? 0,
            counts.FirstOrDefault(c => c.Status == FileStatus.Failed)?.Count ?? 0
        );
    }
}
