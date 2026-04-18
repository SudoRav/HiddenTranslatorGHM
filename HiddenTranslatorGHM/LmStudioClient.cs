using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class LmStudioClient
{
    private readonly HttpClient _http;

    public LmStudioClient(string baseUrl = "http://127.0.0.1:4332")
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public async Task<string> TranslateAsync(string text, string systemPrompt, int maxTokens = 200)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var request = new
        {
            //model = "qwen/qwen3-4b-2507",
            model = "qwen3-4b-instruct-2507@q8_0",
            messages = new[] 
            {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = text }
        },
            max_tokens = maxTokens
        };

        var json = JsonSerializer.Serialize(request);
        var response = await _http.PostAsync("/v1/chat/completions",
            new StringContent(json, Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync();
        Console.WriteLine("LM Studio response: " + responseString);

        try
        {
            using var doc = JsonDocument.Parse(responseString);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
                return "[Ошибка: нет вариантов перевода]";

            var choice = choices[0];
            if (choice.TryGetProperty("message", out var messageProp) &&
                messageProp.TryGetProperty("content", out var contentProp))
                return contentProp.GetString()?.Trim() ?? string.Empty;

            return "[Ошибка: неизвестный формат ответа]";
        }
        catch (Exception ex)
        {
            return $"[Ошибка разбора ответа: {ex.Message}]";
        }
    }

}
