using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ToolDrawingProcessor.Data;
using ToolDrawingProcessor.Models;
using ToolDrawingProcessor.Services;

namespace ToolDrawingProcessor.Pages.Database;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ExcelExportService _excelExport;

    public IndexModel(ApplicationDbContext db, ExcelExportService excelExport)
    {
        _db = db;
        _excelExport = excelExport;
    }

    public List<ToolSpecification> Specs { get; set; } = new();
    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        Specs = await _db.ToolSpecifications
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<IActionResult> OnGetExportExcelAsync()
    {
        try
        {
            var bytes = await _excelExport.ExportAllAsync();
            var fileName = $"ToolSpecifications_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (Exception ex)
        {
            Message = $"Export failed: {ex.Message}";
            await OnGetAsync();
            return Page();
        }
    }
}
