using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ToolDrawingProcessor.Models;

public class AIExtractionLog
{
    [Key]
    public int Id { get; set; }

    public int UploadedFileId { get; set; }

    [ForeignKey(nameof(UploadedFileId))]
    public UploadedFile? UploadedFile { get; set; }

    public string RawAIResponseJson { get; set; } = string.Empty;

    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;
}
