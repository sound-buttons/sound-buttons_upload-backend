using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Serilog.Context;
using SoundButtons.Helper;
using SoundButtons.Models;
using SoundButtons.Services;

namespace SoundButtons.Functions;

public partial class SoundButtons(ILogger<SoundButtons> logger,
                                  ProcessAudioService processAudioService)
{
    private readonly ILogger _logger = logger;

    [Function("sound-buttons")]
    public async Task<HttpResponseData> HttpStart(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)]
        HttpRequestData reqData,
        [DurableClient] DurableTaskClient starter)
    {
        _logger.LogInformation("Content-Type: {contentType}", reqData.Headers.GetValues("Content-Type").FirstOrDefault());

        if (!reqData.Headers.TryGetValues("Content-Type", out IEnumerable<string>? contentTypeValues) ||
            !contentTypeValues.Any(v => v.Contains("multipart/form-data")))
        {
            return CreateBadRequestResponse(reqData, "Invalid content type");
        }

        (Dictionary<string, string> formData, Dictionary<string, byte[]> fileData) = await ParseMultipartFormDataAsync(reqData);

        Source source = SourceCheck(GetSourceInfo(formData));

        if (string.IsNullOrEmpty(source.VideoId)
            && string.IsNullOrEmpty(formData.GetValueOrDefault("clip"))
            && fileData.Count <= 0)
        {
            _logger.LogError("No source found.");
            return CreateBadRequestResponse(reqData, "No source found");
        }

        if (fileData.Count != 0 && fileData.First().Value.Length > 30 * 1024 * 1024)
        {
            _logger.LogError("File size over 30MB.");
            return CreateBadRequestResponse(reqData, "File size over 30MB");
        }

        // 啟動長輪詢
        return await StartOrchestrator(
                   reqData: reqData,
                   starter: starter,
                   filename: GetFileName(formData),
                   directory: formData.GetValueOrDefault("directory") ?? "test",
                   source: source,
                   clip: await ProcessClip(formData, source),
                   toastId: formData.GetValueOrDefault("toastId") ?? "-1",
                   tempPath: await ProcessAudioFileAsync(fileData),
                   ip: reqData.Headers.TryGetValues("X-Forwarded-For", out IEnumerable<string>? forwardedFor) ? forwardedFor.First() : "",
                   nameZH: formData.GetValueOrDefault("nameZH") ?? "",
                   nameJP: formData.GetValueOrDefault("nameJP") ?? "",
                   volume: float.TryParse(formData.GetValueOrDefault("volume"), out float volume) ? volume : 1,
                   group: formData.GetValueOrDefault("group") ?? "未分類"
               );
    }

    private static HttpResponseData CreateBadRequestResponse(HttpRequestData reqData, string message)
    {
        HttpResponseData response = reqData.CreateResponse(HttpStatusCode.BadRequest);
        response.WriteString(message);
        return response;
    }

    private static async Task<(Dictionary<string, string> formData, Dictionary<string, byte[]> fileData)> ParseMultipartFormDataAsync(
        HttpRequestData reqData)
    {
        var formData = new Dictionary<string, string>();
        var fileData = new Dictionary<string, byte[]>();
        string boundary = GetBoundary(reqData.Headers.GetValues("Content-Type").First());
        var reader = new MultipartReader(boundary, reqData.Body);
        while (await reader.ReadNextSectionAsync() is { } section)
        {
            var contentDisposition = ContentDispositionHeaderValue.Parse(section.ContentDisposition);
            if (contentDisposition.DispositionType.Equals("form-data") &&
                !string.IsNullOrEmpty(contentDisposition.Name.ToString()))
            {
                if (contentDisposition.FileName.HasValue)
                {
                    // This is a file upload, handle accordingly
                    using var memoryStream = new MemoryStream();
                    await section.Body.CopyToAsync(memoryStream);
                    fileData[contentDisposition.Name.ToString()] = memoryStream.ToArray();
                }
                else
                {
                    // This is a simple key-value pair
                    using var streamReader = new StreamReader(section.Body);
                    formData[contentDisposition.Name.ToString()] = await streamReader.ReadToEndAsync();
                }
            }
        }

        return (formData, fileData);
    }

    private static string GetBoundary(string contentType)
    {
        string[] elements = contentType.Split(';');
        string element = elements.First(e => e.Trim().StartsWith("boundary="));
        return element.Split('=')[1].Trim('"');
    }

    private async Task<HttpResponseData> StartOrchestrator(HttpRequestData reqData,
                                                           DurableTaskClient starter,
                                                           string filename,
                                                           string directory,
                                                           Source source,
                                                           string? clip,
                                                           string toastId,
                                                           string tempPath,
                                                           string ip,
                                                           string nameZH,
                                                           string nameJP,
                                                           float volume,
                                                           string group)
    {
        var instanceId = Guid.NewGuid().ToString();
        await starter.ScheduleNewOrchestrationInstanceAsync(
            orchestratorName: "main-sound-buttons",
            options: new StartOrchestrationOptions
            {
                InstanceId = instanceId
            },
            input: new Request
            {
                Directory = directory,
                Filename = filename,
                Ip = ip,
                Source = source,
                Group = group,
                NameZH = nameZH,
                NameJP = nameJP,
                Volume = volume,
                TempPath = tempPath,
                ToastId = toastId,
                Clip = clip,
                InstanceId = instanceId
            });

        _logger.LogInformation("Started orchestration with ID {instanceId}.", instanceId);

        return await starter.CreateCheckStatusResponseAsync(reqData, instanceId);
    }

    private string GetFileName(Dictionary<string, string> req)
    {
        string? name = req.GetValueOrDefault("nameZH"); // 用於回傳
        string filename = name ?? "";
        filename = GetFileName().Replace(filename, ""); // 比對通過英數、中日文字等(多位元組字元)
        if (filename.Length == 0)
            filename = Guid.NewGuid().ToString("n");

        _logger.LogInformation("FileName: {filename}", filename);
        return filename;
    }

    private Source GetSourceInfo(Dictionary<string, string> req)
    {
        var source = new Source
        {
            VideoId = req.GetValueOrDefault("videoId") ?? "",
            Start = 0,
            End = 0
        };

        if (double.TryParse(req.GetValueOrDefault("start"), out double start)
            && double.TryParse(req.GetValueOrDefault("end"), out double end))
        {
            source.Start = start;
            source.End = end;
        }

        if (!string.IsNullOrEmpty(source.VideoId) && source.VideoId.StartsWith("http"))
        {
            // Regex for strip youtube video id from url c# and return default thumbnail
            // https://gist.github.com/Flatlineato/f4cc3f3937272646d4b0
            source.VideoId = GetYoutubeVideoId().Match(source.VideoId).Groups[1].Value;

            if (string.IsNullOrEmpty(source.VideoId))
            {
                // Discard unknown source
                source.VideoId = "";
                source.Start = 0;
                source.End = 0;
                _logger.LogError("Discard unknown source: {source}", source.VideoId);
            }

            _logger.LogInformation("Get info from form: {videoId}, {start}, {end}", source.VideoId, source.Start, source.End);
        }

        return source;
    }

    private async Task<string?> ProcessClip(Dictionary<string, string> req, Source source)
    {
        Regex youtubeClipReg = GetYoutubeClip();
        Regex twitchClipReg = GetTwitchClip();

        string? clip = req.GetValueOrDefault("clip");
        if (string.IsNullOrEmpty(clip))
        {
            return null;
        }

        if (youtubeClipReg.IsMatch(clip))
        {
            return await ProcessYoutubeClip(req, source);
        }

        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (twitchClipReg.IsMatch(clip))
        {
            return ProcessTwitchClip(req, source);
        }

        return null;
    }

    private async Task<string?> ProcessYoutubeClip(Dictionary<string, string> req, Source source)
    {
        string? clip = req.GetValueOrDefault("clip");
        Regex clipReg = GetYoutubeClip();

        if (string.IsNullOrEmpty(clip) || !clipReg.IsMatch(clip))
            return clip;

        using HttpClient client = new();
        HttpResponseMessage response = await client.GetAsync(clip);
        string body = await response.Content.ReadAsStringAsync();

        // "clipConfig":{"postId":"UgkxVQpxshiN76QUwblPu-ggj6fl594-ORiU","startTimeMs":"1891037","endTimeMs":"1906037"}
        Regex reg1 = GetYoutubeClipConfigFromHtmlBody();
        Match match1 = reg1.Match(body);
        if (double.TryParse(match1.Groups[1].Value, out double _start)
            && double.TryParse(match1.Groups[2].Value, out double _end))
        {
            source.Start = _start / 1000;
            source.End = _end / 1000;
        }

        // {"videoId":"Gs7QYATahy4"}
        Regex reg2 = GetYoutubeClipVideoIdFromHtmlBody();
        Match match2 = reg2.Match(body);
        source.VideoId = match2.Groups[1].Value;
        _logger.LogInformation("Get info from clip: {videoId}, {start}, {end}", source.VideoId, source.Start, source.End);

        return clip;
    }

    private static string? ProcessTwitchClip(Dictionary<string, string> req, Source source)
    {
        string? clip = req.GetValueOrDefault("clip");
        source.VideoId = string.Empty;
        source.Start = 0;
        source.End = 0;

        return clip;
    }

    private async Task<string> ProcessAudioFileAsync(Dictionary<string, byte[]> req)
        => req.Count > 0
               ? await ProcessAudioFromFileUpload(req)
               : "";

    private Source SourceCheck(Source source)
    {
        if (string.IsNullOrEmpty(source.VideoId))
        {
            source.Start = 0;
            source.End = 0;
            return source;
        }

        if (source.End - source.Start <= 0
            || source.End - source.Start > 180)
        {
            _logger.LogError("Video time invalid: {start}, {end}", source.Start, source.End);
            throw new Exception($"Video time invalid: {source.Start}, {source.End}");
        }

        return source;
    }

    private async Task<string> ProcessAudioFromFileUpload(Dictionary<string, byte[]> files)
    {
        string tempDir = FileHelper.PrepareTempDir();
        string tempPath = Path.Combine(tempDir, DateTime.Now.Ticks + ".tmp");

        _logger.LogInformation("Get file from form post.");

        // 有音檔，直接寫到暫存路徑使用
        KeyValuePair<string, byte[]> file = files.First();
        // Get file info
        string fileExtension = Path.GetExtension(file.Key) ?? "";
        tempPath = Path.ChangeExtension(tempPath, fileExtension);
        _logger.LogInformation("Get extension: {fileExtension}", fileExtension);
        await using (var fs = new FileStream(tempPath, FileMode.OpenOrCreate, FileAccess.Write))
        {
            await fs.WriteAsync(file.Value.AsMemory(0, file.Value.Length));
        }

        _logger.LogInformation("Write file from upload.");

        return await processAudioService.TranscodeAudioAsync(tempPath);
    }

    private static void CleanUp(string tempPath) => File.Delete(tempPath);

    [Function("main-sound-buttons")]
    public async Task<bool> RunOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        Request request = context.GetInput<Request>()!;
        using IDisposable _ = LogContext.PushProperty("InstanceId", request.InstanceId);

        // 若表單有音檔，前一步驟會把檔案放入這個路徑
        if (string.IsNullOrEmpty(request.TempPath))
        {
            request.TempPath = await context.CallActivityAsync<string>("ProcessAudioAsync", request);
        }

        if (!File.Exists(request.TempPath))
        {
            _logger.LogError("File not found: {tempPath}", request.TempPath);
            CleanUp(request.TempPath);
            return false;
        }

        request = await context.CallActivityAsync<Request>("UploadAudioToStorageAsync", request);
        request = await context.CallActivityAsync<Request>("SpeechToTextAsync", request);

        await context.CallActivityAsync("ProcessJsonFile", request);

        CleanUp(request.TempPath);
        _logger.LogInformation("Finish. {filename}", request.NameZH);

        return true;
    }

    [GeneratedRegex(@"[^0-9a-zA-Z\p{L}]+")]
    private static partial Regex GetFileName();

    [GeneratedRegex(
        "https?:\\/\\/(?:[\\w-]+\\.)?(?:youtu\\.be\\/|youtube(?:-nocookie)?\\.com\\S*[^\\w\\s-])([\\w-]{11})(?=[^\\w-]|$)(?![?=&+%\\w.-]*(?:['\"][^<>]*>|<\\/a>))[?=&+%\\w.-]*",
        RegexOptions.IgnoreCase,
        "zh-TW")]
    private static partial Regex GetYoutubeVideoId();

    [GeneratedRegex(@"https?:\/\/(?:[\w-]+\.)?(?:youtu\.be\/|youtube(?:-nocookie)?\.com\/)clip\/[?=&+%\w.-]*")]
    private static partial Regex GetYoutubeClip();

    [GeneratedRegex(@"^(?:https?:\/\/(?:clips\.twitch\.tv\/|www\.twitch\.tv\/[a-z0-9_-]+\/clip\/))([a-zA-Z0-9_-]+)$")]
    private static partial Regex GetTwitchClip();

    [GeneratedRegex(@"clipConfig"":{""postId"":""(?:[\w-]+)"",""startTimeMs"":""(\d+)"",""endTimeMs"":""(\d+)""}")]
    private static partial Regex GetYoutubeClipConfigFromHtmlBody();

    [GeneratedRegex(@"{""videoId"":""([\w-]+)""")]
    private static partial Regex GetYoutubeClipVideoIdFromHtmlBody();
}