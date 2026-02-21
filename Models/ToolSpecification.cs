using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ToolDrawingProcessor.Models;

public class ToolSpecification
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(500)]
    public string SourceFileName { get; set; } = string.Empty;

    public int UploadedFileId { get; set; }

    [ForeignKey(nameof(UploadedFileId))]
    public UploadedFile? UploadedFile { get; set; }

    [MaxLength(100)]
    public string? ToolType { get; set; }

    public double? Diameter { get; set; }
    public double? FluteLength { get; set; }
    public double? CornerRadius { get; set; }
    public double? ShankDiameter { get; set; }
    public double? TotalLength { get; set; }
    public int? NumberOfFlutes { get; set; }

    [Range(0.0, 1.0)]
    public double OverallConfidence { get; set; }

    [MaxLength(100)]
    public string? AIProviderUsed { get; set; }

    [MaxLength(100)]
    public string? ModelUsed { get; set; }

    public int CorrectionCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
