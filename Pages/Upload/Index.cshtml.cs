using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ToolDrawingProcessor.Data;
using ToolDrawingProcessor.Models;

namespace ToolDrawingProcessor.Pages.Upload;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ApplicationDbContext db, IWebHostEnvironment env, ILogger<IndexModel> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    public string? Message { get; set; }
    public bool IsError { get; set; }
    public List<UploadedFile> PendingFiles { get; set; } = new();

    public async Task OnGetAsync()
    {
        PendingFiles = await _db.UploadedFiles
            .Where(f => f.Status == FileStatus.Pending)
            .OrderByDescending(f => f.UploadedAt)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync(List<IFormFile> files)
    {
        if (files == null || files.Count == 0)
        {
            Message = "Please select at least one PDF file.";
            IsError = true;
            await OnGetAsync();
            return Page();
        }

        var uploadsPath = Path.Combine(_env.WebRootPath, "uploads");
        Directory.CreateDirectory(uploadsPath);

        int uploadedCount = 0;
        var errors = new List<string>();

        foreach (var file in files)
        {
            try
            {
                if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{file.FileName}: Not a PDF file.");
                    continue;
                }

                if (file.Length == 0)
                {
                    errors.Add($"{file.FileName}: File is empty.");
                    continue;
                }

                // Generate unique filename
                var uniqueName = $"{Guid.NewGuid()}_{file.FileName}";
                var filePath = Path.Combine(uploadsPath, uniqueName);

                await using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var uploadedFile = new UploadedFile
                {
                    FileName = file.FileName,
                    FilePath = filePath,
                    Status = FileStatus.Pending,
                    UploadedAt = DateTime.UtcNow
                };

                _db.UploadedFiles.Add(uploadedFile);
                uploadedCount++;

                _logger.LogInformation("Uploaded file: {FileName}", file.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file: {FileName}", file.FileName);
                errors.Add($"{file.FileName}: {ex.Message}");
            }
        }

        await _db.SaveChangesAsync();

        if (errors.Any())
        {
            Message = $"Uploaded {uploadedCount} file(s). Errors: {string.Join("; ", errors)}";
            IsError = uploadedCount == 0;
        }
        else
        {
            Message = $"Successfully uploaded {uploadedCount} file(s). Go to Processing to extract data.";
            IsError = false;
        }

        await OnGetAsync();
        return Page();
    }
}
