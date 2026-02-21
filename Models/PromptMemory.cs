using System.ComponentModel.DataAnnotations;

namespace ToolDrawingProcessor.Models;

public class PromptMemory
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(2000)]
    public string RuleText { get; set; } = string.Empty;

    public int UsageCount { get; set; }

    public DateTime LastUsed { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
