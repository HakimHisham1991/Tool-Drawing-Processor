using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace ToolDrawingProcessor.Services;

/// <summary>
/// Extracts text content from PDF files using PdfPig.
/// </summary>
public class PdfTextExtractorService
{
    private readonly ILogger<PdfTextExtractorService> _logger;

    public PdfTextExtractorService(ILogger<PdfTextExtractorService> logger)
    {
        _logger = logger;
    }

    public async Task<string> ExtractTextAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Extracting text from PDF: {FilePath}", filePath);

                using var document = PdfDocument.Open(filePath);
                var textParts = new List<string>();

                foreach (var page in document.GetPages())
                {
                    var pageText = page.Text;
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        textParts.Add(pageText);
                    }

                    // Also extract text from individual words for better layout
                    var words = page.GetWords();
                    if (words.Any())
                    {
                        var wordText = string.Join(" ", words.Select(w => w.Text));
                        if (!string.IsNullOrWhiteSpace(wordText) && wordText != pageText)
                        {
                            textParts.Add($"[Word-level extraction]: {wordText}");
                        }
                    }
                }

                var result = string.Join("\n\n--- Page Break ---\n\n", textParts);

                if (string.IsNullOrWhiteSpace(result))
                {
                    _logger.LogWarning("No text extracted from PDF: {FilePath}. The PDF may contain only images.", filePath);
                    return "[No text content found - PDF may contain only images/drawings. AI will analyze based on common tooling drawing patterns.]";
                }

                _logger.LogInformation("Successfully extracted {Length} characters from PDF", result.Length);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from PDF: {FilePath}", filePath);
                throw;
            }
        });
    }
}
