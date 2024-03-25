using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;
using static SoundButtons.Models.OpenAI;
using Log = SoundButtons.Helper.Log;

namespace SoundButtons.Services;

internal class OpenAIService
{
    private static string? _apiKey = "";
    private readonly HttpClient _client;

    public OpenAIService()
    {
        _client = new HttpClient
        {
            BaseAddress = new Uri(OpenAIEndpoint)
        };

        _apiKey = Environment.GetEnvironmentVariable("OpenAI_ApiKey");
        if (string.IsNullOrEmpty(_apiKey))
        {
            Logger.Fatal("OpenAI api key is not set.");
        }
    }

    private static ILogger Logger => Log.Logger;
    private string OpenAIEndpoint = "https://api.openai.com/v1/";

    /// <summary>
    ///     Get speech to text result
    /// </summary>
    /// <param name="path">Audio file path to process</param>
    /// <param name="language">Specified language</param>
    /// <exception cref="HttpRequestException">Failed to get speech to text result.</exception>
    /// <returns></returns>
    public async Task<TranscriptionsResponse?> SpeechToTextAsync(string path, string language = "")
    {
        if (!CheckApiKey()) return new TranscriptionsResponse();

        await using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var content = new MultipartFormDataContent
        {
            { new StreamContent(fileStream), "file", Path.GetFileName(path) },
            { new StringContent("whisper-1"), "model" },
            { new StringContent("Remove superfluous words"), "prompt" },
            { new StringContent("verbose_json"), "response_format" },
            { new StringContent("0.1"), "temperature" }
        };

        if (!string.IsNullOrEmpty(language))
        {
            content.Add(new StringContent(language), "language");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");
        request.Content = content;
        using HttpResponseMessage response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<TranscriptionsResponse>(json);
    }

    private static bool CheckApiKey() => !string.IsNullOrEmpty(_apiKey);
}