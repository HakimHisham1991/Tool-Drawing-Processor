using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ToolDrawingProcessor.Data;

namespace ToolDrawingProcessor.Services;

/// <summary>
/// Exports tool specifications to Excel using ClosedXML.
/// </summary>
public class ExcelExportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExcelExportService> _logger;

    public ExcelExportService(
        IServiceScopeFactory scopeFactory,
        ILogger<ExcelExportService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<byte[]> ExportAllAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var specs = await db.ToolSpecifications
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Tool Specifications");

        // Headers
        var headers = new[]
        {
            "ID", "Source File", "Tool Type", "Diameter (mm)", "Flute Length (mm)",
            "Corner Radius (mm)", "Shank Diameter (mm)", "Total Length (mm)",
            "Number of Flutes", "Overall Confidence", "AI Provider", "Model Used",
            "Correction Count", "Created At"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = worksheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.DarkBlue;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Data rows
        for (int row = 0; row < specs.Count; row++)
        {
            var spec = specs[row];
            var r = row + 2;

            worksheet.Cell(r, 1).Value = spec.Id;
            worksheet.Cell(r, 2).Value = spec.SourceFileName;
            worksheet.Cell(r, 3).Value = spec.ToolType ?? "";
            worksheet.Cell(r, 4).Value = spec.Diameter ?? 0;
            worksheet.Cell(r, 5).Value = spec.FluteLength ?? 0;
            worksheet.Cell(r, 6).Value = spec.CornerRadius ?? 0;
            worksheet.Cell(r, 7).Value = spec.ShankDiameter ?? 0;
            worksheet.Cell(r, 8).Value = spec.TotalLength ?? 0;
            worksheet.Cell(r, 9).Value = spec.NumberOfFlutes ?? 0;
            worksheet.Cell(r, 10).Value = spec.OverallConfidence;
            worksheet.Cell(r, 11).Value = spec.AIProviderUsed ?? "";
            worksheet.Cell(r, 12).Value = spec.ModelUsed ?? "";
            worksheet.Cell(r, 13).Value = spec.CorrectionCount;
            worksheet.Cell(r, 14).Value = spec.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");

            // Confidence formatting
            var confCell = worksheet.Cell(r, 10);
            confCell.Style.NumberFormat.Format = "0.00%";
            if (spec.OverallConfidence < 0.6)
                confCell.Style.Font.FontColor = XLColor.Red;
            else if (spec.OverallConfidence < 0.85)
                confCell.Style.Font.FontColor = XLColor.DarkOrange;

            // Correction count color coding
            var corrCell = worksheet.Cell(r, 13);
            if (spec.CorrectionCount == 0)
                corrCell.Style.Font.FontColor = XLColor.Green;
            else if (spec.CorrectionCount <= 2)
                corrCell.Style.Font.FontColor = XLColor.DarkOrange;
            else
                corrCell.Style.Font.FontColor = XLColor.Red;
        }

        // Auto-fit columns
        worksheet.Columns().AdjustToContents();

        // Add filter
        worksheet.RangeUsed()?.SetAutoFilter();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
