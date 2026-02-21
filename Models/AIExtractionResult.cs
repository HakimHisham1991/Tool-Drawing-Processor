using System.Text.Json.Serialization;

namespace ToolDrawingProcessor.Models;

/// <summary>
/// DTO representing the structured AI extraction output.
/// </summary>
public class AIExtractionResult
{
    [JsonPropertyName("toolType")]
    public FieldResult<string> ToolType { get; set; } = new();

    [JsonPropertyName("diameter")]
    public FieldResult<double?> Diameter { get; set; } = new();

    [JsonPropertyName("fluteLength")]
    public FieldResult<double?> FluteLength { get; set; } = new();

    [JsonPropertyName("cornerRadius")]
    public FieldResult<double?> CornerRadius { get; set; } = new();

    [JsonPropertyName("shankDiameter")]
    public FieldResult<double?> ShankDiameter { get; set; } = new();

    [JsonPropertyName("totalLength")]
    public FieldResult<double?> TotalLength { get; set; } = new();

    [JsonPropertyName("numberOfFlutes")]
    public FieldResult<int?> NumberOfFlutes { get; set; } = new();
}

public class FieldResult<T>
{
    [JsonPropertyName("value")]
    public T? Value { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}

/// <summary>
/// Settings for the AI provider configuration.
/// </summary>
public record AIProviderSettings
{
    public string Provider { get; set; } = "ollama";       // ollama, openai, anthropic
    public string Model { get; set; } = "llama3";
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string? OpenAIApiKey { get; set; }
    public string? AnthropicApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
}
