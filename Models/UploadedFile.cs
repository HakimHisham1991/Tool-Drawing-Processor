using System.ComponentModel.DataAnnotations;

namespace ToolDrawingProcessor.Models;

public enum FileStatus
{
    Pending,
    Processing,
    Processed,
    Failed
}

public class UploadedFile
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(500)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string FilePath { get; set; } = string.Empty;

    public FileStatus Status { get; set; } = FileStatus.Pending;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    // Navigation
    public ToolSpecification? ToolSpecification { get; set; }
    public AIExtractionLog? AIExtractionLog { get; set; }
    public ICollection<CorrectionFeedback> CorrectionFeedbacks { get; set; } = new List<CorrectionFeedback>();
}
