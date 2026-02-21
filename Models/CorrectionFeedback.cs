using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ToolDrawingProcessor.Models;

public class CorrectionFeedback
{
    [Key]
    public int Id { get; set; }

    public int UploadedFileId { get; set; }

    [ForeignKey(nameof(UploadedFileId))]
    public UploadedFile? UploadedFile { get; set; }

    [Required]
    [MaxLength(100)]
    public string FieldName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? AIValue { get; set; }

    [MaxLength(500)]
    public string? CorrectedValue { get; set; }

    public bool WasCorrect { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
