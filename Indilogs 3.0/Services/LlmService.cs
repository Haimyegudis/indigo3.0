using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json; // אם חסר, התקן דרך NuGet את System.Text.Json
using System.Threading.Tasks;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Services
{
    public class LlmService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        private const string OllamaUrl = "http://localhost:11434/api/generate";

        public string GetAiAnalysis(List<LogEntry> logs, string failureContext)
        {
            try
            {
                // 1. בניית הפרומפט
                var sb = new StringBuilder();
                sb.AppendLine($"Analyze the following log failure context: {failureContext}");
                sb.AppendLine("Identify the root cause and suggest a fix. Be concise.");
                sb.AppendLine("LOGS:");

                foreach (var log in logs)
                {
                    sb.AppendLine($"[{log.Date:HH:mm:ss}] [{log.Level}] {log.ProcessName}: {log.Message}");
                }

                var requestBody = new
                {
                    model = "llama3", // וודא שזה השם של המודל שהורדת
                    prompt = sb.ToString(),
                    stream = false
                };

                var jsonContent = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // 2. שליחה סינכרונית (כדי לא לשבור את הממשק הקיים)
                var response = _httpClient.PostAsync(OllamaUrl, content).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                var responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(responseString);
                return doc.RootElement.GetProperty("response").GetString();
            }
            catch (Exception ex)
            {
                return $"AI Analysis Error: {ex.Message}";
            }
        }
    }
}