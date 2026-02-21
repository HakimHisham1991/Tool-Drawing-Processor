using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ToolDrawingProcessor.Models;

namespace ToolDrawingProcessor.Services;

/// <summary>
/// Handles AI provider communication (Ollama, OpenAI, Anthropic)
/// with dynamic prompt reinforcement from the learning engine.
/// </summary>
public class AIProviderService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LearningEngineService _learningEngine;
    private readonly ILogger<AIProviderService> _logger;
    private readonly IConfiguration _configuration;

    public AIProviderService(
        IHttpClientFactory httpClientFactory,
        LearningEngineService learningEngine,
        ILogger<AIProviderService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _learningEngine = learningEngine;
        _logger = logger;
        _configuration = configuration;
    }

    public AIProviderSettings GetSettings()
    {
        var settings = new AIProviderSettings();
        _configuration.GetSection("AIProvider").Bind(settings);
        return settings;
    }

    /// <summary>
    /// Extract tooling specifications from PDF text using the configured AI provider.
    /// </summary>
    public async Task<(AIExtractionResult result, string rawJson, string provider, string model)> ExtractSpecificationsAsync(
        string pdfText, AIProviderSettings? settingsOverride = null)
    {
        var settings = settingsOverride ?? GetSettings();
        var reinforcementRules = await _learningEngine.GenerateReinforcementRulesAsync();

        var prompt = BuildPrompt(pdfText, reinforcementRules);

        _logger.LogInformation("Sending extraction request to {Provider}/{Model}", settings.Provider, settings.Model);

        string rawResponse;

        try
        {
            rawResponse = settings.Provider.ToLowerInvariant() switch
            {
                "ollama" => await CallOllamaAsync(prompt, settings),
                "openai" => await CallOpenAIAsync(prompt, settings),
                "anthropic" => await CallAnthropicAsync(prompt, settings),
                _ => throw new ArgumentException($"Unknown AI provider: {settings.Provider}")
            };
        }
        catch (OllamaServerException ex) when (settings.Provider == "ollama")
        {
            _logger.LogError(ex, "Ollama returned a server error");
            throw new InvalidOperationException(ex.Message, ex);
        }
        catch (Exception ex) when (settings.Provider == "ollama" && ex is not OllamaServerException)
        {
            _logger.LogWarning(ex, "Ollama is unreachable. Checking for cloud fallback...");

            if (!string.IsNullOrEmpty(settings.OpenAIApiKey))
            {
                _logger.LogInformation("Falling back to OpenAI...");
                settings = settings with { Provider = "openai", Model = "gpt-4o-mini" };
                rawResponse = await CallOpenAIAsync(prompt, settings);
            }
            else if (!string.IsNullOrEmpty(settings.AnthropicApiKey))
            {
                _logger.LogInformation("Falling back to Anthropic...");
                settings = settings with { Provider = "anthropic", Model = "claude-3-haiku-20240307" };
                rawResponse = await CallAnthropicAsync(prompt, settings);
            }
            else
            {
                throw new InvalidOperationException(
                    "Ollama is not reachable and no cloud fallback is configured. " +
                    "Please ensure Ollama is running or configure an API key in Settings.", ex);
            }
        }

        var result = ParseAIResponse(rawResponse);
        return (result, rawResponse, settings.Provider, settings.Model);
    }

    private string BuildPrompt(string pdfText, string reinforcementRules)
    {
        return $@"You are a CNC tooling specification extraction engine.
Your task is to extract structured tooling data from the provided PDF text content.

{reinforcementRules}

Extract the following fields:
1. Tool Type (e.g., Endmill, Ball Nose, Drill, Reamer, Chamfer Mill, Face Mill, Tap, Boring Bar)
2. Diameter (mm)
3. Flute Length (mm)
4. Corner Radius (mm) - use 0 if not applicable
5. Shank Diameter (mm)
6. Total Length (mm)
7. Number of Flutes

For each field, return a confidence score between 0.0 and 1.0.

IMPORTANT RULES:
- Shank diameter is usually the same or larger than cutting diameter.
- Endmills typically have a flute count between 2 and 6.
- Drills typically have 2 flutes.
- Values should be in millimeters. Convert from inches if needed (multiply by 25.4).
- If a value is not found, set it to null with confidence 0.0.
- Return ONLY valid JSON, no other text.

Return JSON in exactly this format:
{{
  ""toolType"": {{ ""value"": ""Endmill"", ""confidence"": 0.95 }},
  ""diameter"": {{ ""value"": 12.0, ""confidence"": 0.9 }},
  ""fluteLength"": {{ ""value"": 25.0, ""confidence"": 0.85 }},
  ""cornerRadius"": {{ ""value"": 0.5, ""confidence"": 0.8 }},
  ""shankDiameter"": {{ ""value"": 12.0, ""confidence"": 0.9 }},
  ""totalLength"": {{ ""value"": 75.0, ""confidence"": 0.85 }},
  ""numberOfFlutes"": {{ ""value"": 4, ""confidence"": 0.95 }}
}}

PDF TEXT:
{pdfText}";
    }

    private async Task<string> CallOllamaAsync(string prompt, AIProviderSettings settings)
    {
        var client = _httpClientFactory.CreateClient("Ollama");
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

        var requestBody = new
        {
            model = settings.Model,
            prompt = prompt,
            stream = false,
            options = new { temperature = 0.1 }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Calling Ollama at {Endpoint}/api/generate with model {Model}", settings.OllamaEndpoint, settings.Model);

        var response = await client.PostAsync($"{settings.OllamaEndpoint}/api/generate", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("Ollama returned {StatusCode}: {ErrorBody}", (int)response.StatusCode, errorBody);

            var errorMessage = $"Ollama returned HTTP {(int)response.StatusCode}";
            try
            {
                using var errorDoc = JsonDocument.Parse(errorBody);
                if (errorDoc.RootElement.TryGetProperty("error", out var errorProp))
                    errorMessage = $"Ollama error: {errorProp.GetString()}";
            }
            catch { /* use generic message */ }

            throw new OllamaServerException(errorMessage);
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement.GetProperty("response").GetString()
               ?? throw new InvalidOperationException("Empty response from Ollama");
    }

    public class OllamaServerException : Exception
    {
        public OllamaServerException(string message) : base(message) { }
    }

    private async Task<string> CallOpenAIAsync(string prompt, AIProviderSettings settings)
    {
        var client = _httpClientFactory.CreateClient("OpenAI");
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.OpenAIApiKey}");

        var requestBody = new
        {
            model = settings.Model,
            messages = new[]
            {
                new { role = "system", content = "You are a CNC tooling specification extraction engine. Return only valid JSON." },
                new { role = "user", content = prompt }
            },
            temperature = 0.1,
            max_tokens = 1000
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()
            ?? throw new InvalidOperationException("Empty response from OpenAI");
    }

    private async Task<string> CallAnthropicAsync(string prompt, AIProviderSettings settings)
    {
        var client = _httpClientFactory.CreateClient("Anthropic");
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        client.DefaultRequestHeaders.Add("x-api-key", settings.AnthropicApiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var requestBody = new
        {
            model = settings.Model,
            max_tokens = 1000,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("https://api.anthropic.com/v1/messages", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()
            ?? throw new InvalidOperationException("Empty response from Anthropic");
    }

    private AIExtractionResult ParseAIResponse(string rawResponse)
    {
        try
        {
            // Try to extract JSON from the response (AI sometimes wraps it in markdown)
            var jsonMatch = Regex.Match(rawResponse, @"\{[\s\S]*\}", RegexOptions.Multiline);
            if (!jsonMatch.Success)
            {
                _logger.LogWarning("Could not find JSON in AI response, returning defaults");
                return CreateDefaultResult();
            }

            var jsonStr = jsonMatch.Value;

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            };

            var result = JsonSerializer.Deserialize<AIExtractionResult>(jsonStr, options);
            return result ?? CreateDefaultResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse AI response: {Response}", rawResponse);
            return CreateDefaultResult();
        }
    }

    private static AIExtractionResult CreateDefaultResult()
    {
        return new AIExtractionResult
        {
            ToolType = new FieldResult<string> { Value = "Unknown", Confidence = 0.0 },
            Diameter = new FieldResult<double?> { Value = null, Confidence = 0.0 },
            FluteLength = new FieldResult<double?> { Value = null, Confidence = 0.0 },
            CornerRadius = new FieldResult<double?> { Value = null, Confidence = 0.0 },
            ShankDiameter = new FieldResult<double?> { Value = null, Confidence = 0.0 },
            TotalLength = new FieldResult<double?> { Value = null, Confidence = 0.0 },
            NumberOfFlutes = new FieldResult<int?> { Value = null, Confidence = 0.0 }
        };
    }

    /// <summary>
    /// Check if Ollama is reachable.
    /// </summary>
    public async Task<bool> IsOllamaOnlineAsync()
    {
        try
        {
            var settings = GetSettings();
            var client = _httpClientFactory.CreateClient("Ollama");
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync($"{settings.OllamaEndpoint}/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get available Ollama models.
    /// </summary>
    public async Task<List<string>> GetOllamaModelsAsync()
    {
        try
        {
            var settings = GetSettings();
            var client = _httpClientFactory.CreateClient("Ollama");
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync($"{settings.OllamaEndpoint}/api/tags");

            if (!response.IsSuccessStatusCode) return new List<string>();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            return doc.RootElement.GetProperty("models")
                .EnumerateArray()
                .Select(m => m.GetProperty("name").GetString() ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }
}
