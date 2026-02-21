using Microsoft.EntityFrameworkCore;
using ToolDrawingProcessor.Data;
using ToolDrawingProcessor.Models;

namespace ToolDrawingProcessor.Services;

/// <summary>
/// Self-learning engine that generates dynamic prompt reinforcement rules
/// based on past correction feedback.
/// </summary>
public class LearningEngineService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LearningEngineService> _logger;

    public LearningEngineService(
        IServiceScopeFactory scopeFactory,
        ILogger<LearningEngineService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Generates dynamic reinforcement rules from correction history.
    /// </summary>
    public async Task<string> GenerateReinforcementRulesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rules = new List<string>();

        // 1. Get stored prompt memory rules
        var memoryRules = await db.PromptMemories
            .OrderByDescending(p => p.UsageCount)
            .Take(10)
            .ToListAsync();

        foreach (var rule in memoryRules)
        {
            rules.Add(rule.RuleText);
            rule.UsageCount++;
            rule.LastUsed = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();

        // 2. Analyze correction patterns
        var corrections = await db.CorrectionFeedbacks
            .Where(c => !c.WasCorrect)
            .GroupBy(c => c.FieldName)
            .Select(g => new
            {
                Field = g.Key,
                Count = g.Count(),
                Samples = g.OrderByDescending(x => x.CreatedAt)
                           .Take(5)
                           .Select(x => new { x.AIValue, x.CorrectedValue })
                           .ToList()
            })
            .ToListAsync();

        foreach (var correction in corrections)
        {
            if (correction.Count >= 2)
            {
                var sampleText = string.Join("; ",
                    correction.Samples.Select(s =>
                        $"AI predicted '{s.AIValue}' but correct was '{s.CorrectedValue}'"));

                rules.Add(
                    $"FIELD '{correction.Field}' has been corrected {correction.Count} times. " +
                    $"Common pattern: {sampleText}. Pay extra attention to this field.");
            }
        }

        // 3. Detect decimal misplacement patterns
        var decimalIssues = await db.CorrectionFeedbacks
            .Where(c => !c.WasCorrect &&
                         c.AIValue != null && c.CorrectedValue != null)
            .ToListAsync();

        foreach (var issue in decimalIssues)
        {
            if (double.TryParse(issue.AIValue, out var aiVal) &&
                double.TryParse(issue.CorrectedValue, out var corrVal))
            {
                if (Math.Abs(aiVal * 10 - corrVal) < 0.01 ||
                    Math.Abs(aiVal / 10 - corrVal) < 0.01)
                {
                    rules.Add(
                        $"WARNING: Decimal misplacement detected for {issue.FieldName}. " +
                        $"AI read {issue.AIValue} but correct was {issue.CorrectedValue}. " +
                        "Verify decimal positions carefully against common tooling sizes.");
                }
            }
        }

        // 4. Tool type confusion detection
        var toolTypeCorrections = await db.CorrectionFeedbacks
            .Where(c => !c.WasCorrect && c.FieldName == "ToolType")
            .GroupBy(c => new { c.AIValue, c.CorrectedValue })
            .Where(g => g.Count() >= 2)
            .Select(g => new { g.Key.AIValue, g.Key.CorrectedValue, Count = g.Count() })
            .ToListAsync();

        foreach (var tc in toolTypeCorrections)
        {
            rules.Add(
                $"AI frequently misclassifies '{tc.AIValue}' as '{tc.CorrectedValue}' " +
                $"({tc.Count} occurrences). Double-check tool type classification.");
        }

        if (rules.Count == 0)
        {
            return string.Empty;
        }

        var reinforcementBlock = "\n\n---\nIMPORTANT EXTRACTION RULES (Learned from previous corrections):\n" +
                                 string.Join("\n- ", rules.Prepend("")) +
                                 "\n---\n";

        _logger.LogInformation("Generated {Count} reinforcement rules for AI prompt", rules.Count);
        return reinforcementBlock;
    }

    /// <summary>
    /// Records correction feedback and auto-generates rules when thresholds are met.
    /// </summary>
    public async Task RecordCorrectionsAsync(
        int uploadedFileId,
        AIExtractionResult aiResult,
        ToolSpecification accepted)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var fields = new Dictionary<string, (string? aiValue, string? correctedValue)>
        {
            ["ToolType"] = (aiResult.ToolType.Value, accepted.ToolType),
            ["Diameter"] = (aiResult.Diameter.Value?.ToString(), accepted.Diameter?.ToString()),
            ["FluteLength"] = (aiResult.FluteLength.Value?.ToString(), accepted.FluteLength?.ToString()),
            ["CornerRadius"] = (aiResult.CornerRadius.Value?.ToString(), accepted.CornerRadius?.ToString()),
            ["ShankDiameter"] = (aiResult.ShankDiameter.Value?.ToString(), accepted.ShankDiameter?.ToString()),
            ["TotalLength"] = (aiResult.TotalLength.Value?.ToString(), accepted.TotalLength?.ToString()),
            ["NumberOfFlutes"] = (aiResult.NumberOfFlutes.Value?.ToString(), accepted.NumberOfFlutes?.ToString())
        };

        int correctionCount = 0;

        foreach (var (fieldName, (aiValue, correctedValue)) in fields)
        {
            var wasCorrect = NormalizeCompare(aiValue, correctedValue);

            if (!wasCorrect) correctionCount++;

            db.CorrectionFeedbacks.Add(new CorrectionFeedback
            {
                UploadedFileId = uploadedFileId,
                FieldName = fieldName,
                AIValue = aiValue,
                CorrectedValue = correctedValue,
                WasCorrect = wasCorrect,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();

        // Auto-generate rules if threshold is met
        await AutoGenerateRulesAsync(db);

        _logger.LogInformation(
            "Recorded {Total} feedback entries for file {FileId}, {Corrections} corrections",
            fields.Count, uploadedFileId, correctionCount);
    }

    private static bool NormalizeCompare(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b))
            return true;

        if (a == null || b == null)
            return a == b;

        // Try numeric comparison for tolerance
        if (double.TryParse(a, out var numA) && double.TryParse(b, out var numB))
        {
            return Math.Abs(numA - numB) < 0.001;
        }

        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task AutoGenerateRulesAsync(ApplicationDbContext db)
    {
        // Check for fields with > 5 corrections
        var frequentErrors = await db.CorrectionFeedbacks
            .Where(c => !c.WasCorrect)
            .GroupBy(c => c.FieldName)
            .Where(g => g.Count() > 5)
            .Select(g => new { Field = g.Key, Count = g.Count() })
            .ToListAsync();

        foreach (var error in frequentErrors)
        {
            // Check if rule already exists
            var existingRule = await db.PromptMemories
                .FirstOrDefaultAsync(p => p.RuleText.Contains(error.Field));

            if (existingRule == null)
            {
                var samples = await db.CorrectionFeedbacks
                    .Where(c => !c.WasCorrect && c.FieldName == error.Field)
                    .OrderByDescending(c => c.CreatedAt)
                    .Take(3)
                    .ToListAsync();

                var sampleText = string.Join(", ",
                    samples.Select(s => $"'{s.AIValue}'→'{s.CorrectedValue}'"));

                var ruleText = $"AI frequently misreads {error.Field} " +
                               $"(corrected {error.Count}+ times). Examples: {sampleText}. " +
                               $"Verify {error.Field} values carefully.";

                db.PromptMemories.Add(new PromptMemory
                {
                    RuleText = ruleText,
                    UsageCount = 0,
                    CreatedAt = DateTime.UtcNow
                });

                _logger.LogInformation("Auto-generated rule for field: {Field}", error.Field);
            }
        }

        await db.SaveChangesAsync();
    }
}
