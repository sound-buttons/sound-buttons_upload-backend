using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using SoundButtons.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SoundButtons;

public partial class SoundButtons
{
    internal static string _tempDir = @"C:\home\data\SoundButtons";

    public SoundButtons()
    {
        PrepareTempDir();
    }

    public static void PrepareTempDir()
    {
#if DEBUG
        string tempDir = Path.Combine(Path.GetTempPath(), "SoundButtons");
#else
        string tempDir = _tempDir;
#endif
        Directory.CreateDirectory(tempDir); // For safety
        _tempDir = tempDir;
    }

    [FunctionName("sound-buttons")]
    public static async Task<IActionResult> HttpStart(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
        [DurableClient] IDurableOrchestrationClient starter,
        ILogger log)
    {
        // 驗證ContentType為multipart/form-data
        string contentType = req.ContentType;
        log.LogInformation($"Content-Type: {contentType}");
        if (!contentType.Contains("multipart/form-data;"))
            return (ActionResult)new BadRequestResult();

        if (!req.Form.ContainsKey("nameZH"))
            return (ActionResult)new BadRequestResult();

        Source source = GetSourceInfo(req, log);
        string clip = await ProcessYoutubeClip(req, log, source);

        string tempPath = "";
        try
        {
            tempPath = await ProcessAudioFileAsync(req, log, source);
        }
        catch (Exception e)
        {
            log.LogError("ProcessAudioFileAsync: {exception}, {message}, {stacktrace}", e, e.Message, e.StackTrace);
            return (ActionResult)new BadRequestResult();
        }

        if (!float.TryParse(req.Form.GetFirstValue("volume"), out float volume)) { volume = 1; }

        // 啟動長輪詢
        IActionResult orchestratorResult = await StartOrchestrator(req: req,
                                                                   starter: starter,
                                                                   log: log,
                                                                   filename: GetFileName(req, log),
                                                                   directory: req.Form.GetFirstValue("directory") ?? "test",
                                                                   source: source,
                                                                   clip: clip,
                                                                   // toast ID用於回傳，讓前端能取消顯示toast
                                                                   toastId: req.Form.GetFirstValue("toastId") ?? "-1",
                                                                   tempPath: tempPath,
                                                                   ip: req.Headers.FirstOrDefault(x => x.Key == "X-Forwarded-For").Value.FirstOrDefault(),
                                                                   nameZH: req.Form.GetFirstValue("nameZH") ?? "",
                                                                   nameJP: req.Form.GetFirstValue("nameJP") ?? "",
                                                                   volume: volume,
                                                                   group: req.Form.GetFirstValue("group") ?? "未分類");
        return orchestratorResult;
    }

    private static async Task<IActionResult> StartOrchestrator(HttpRequest req, IDurableOrchestrationClient starter, ILogger log, string filename, string directory, Source source, string clip, string toastId, string tempPath, string ip, string nameZH, string nameJP, float volume, string group)
    {
        string instanceId = await starter.StartNewAsync(
            orchestratorFunctionName: "main-sound-buttons",
            instanceId: null,
            input: new Request()
            {
                directory = directory,
                filename = filename,
                ip = ip,
                source = source,
                group = group,
                nameZH = nameZH,
                nameJP = nameJP,
                volume = volume,
                tempPath = tempPath,
                toastId = toastId,
                clip = clip
            });

        log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

        return starter.CreateCheckStatusResponse(req, instanceId, true);
    }

    private static string GetFileName(HttpRequest req, ILogger log)
    {
        string name = req.Form.GetFirstValue("nameZH"); // 用於回傳
        string filename = name ?? "";
        filename = Regex.Replace(filename, @"[^0-9a-zA-Z\p{L}]+", ""); // 比對通過英數、中日文字等(多位元組字元)
        if (filename.Length == 0)
            filename = Guid.NewGuid().ToString("n");
        log.LogInformation("FileName: {filename}", filename);
        return filename;
    }

    private static Source GetSourceInfo(HttpRequest req, ILogger log)
    {
        var source = new Source
        {
            videoId = req.Form.GetFirstValue("videoId") ?? "",
            start = 0,
            end = 0
        };
        if (double.TryParse(req.Form.GetFirstValue("start"), out double start)
            && double.TryParse(req.Form.GetFirstValue("end"), out double end))
        {
            source.start = start;
            source.end = end;
        }

        if (!string.IsNullOrEmpty(source.videoId) && source.videoId.StartsWith("http"))
        {
            // Regex for strip youtube video id from url c# and returl default thumbnail
            // https://gist.github.com/Flatlineato/f4cc3f3937272646d4b0
            source.videoId = Regex.Match(
                source.videoId,
                "https?:\\/\\/(?:[\\w-]+\\.)?(?:youtu\\.be\\/|youtube(?:-nocookie)?\\.com\\S*[^\\w\\s-])([\\w-]{11})(?=[^\\w-]|$)(?![?=&+%\\w.-]*(?:['\"][^<>]*>|<\\/a>))[?=&+%\\w.-]*",
                RegexOptions.IgnoreCase).Groups[1].Value;

            if (string.IsNullOrEmpty(source.videoId))
            {
                // Discard unknown source
                source.videoId = "";
                source.start = 0;
                source.end = 0;
                log.LogError("Discard unknown source: {source}", source.videoId);
            }
            log.LogInformation("Get info from form: {videoId}, {start}, {end}", source.videoId, source.start, source.end);
        }

        return source;
    }

    private static async Task<string> ProcessYoutubeClip(HttpRequest req, ILogger log, Source source)
    {
        string clip = req.Form.GetFirstValue("clip");
        Regex clipReg = new(@"https?:\/\/(?:[\w-]+\.)?(?:youtu\.be\/|youtube(?:-nocookie)?\.com\/)clip\/[?=&+%\w.-]*");
        if (!string.IsNullOrEmpty(clip) && clipReg.IsMatch(clip))
        {
            using HttpClient client = new();
            var response = await client.GetAsync(clip);
            string body = await response.Content.ReadAsStringAsync();

            // "clipConfig":{"postId":"UgkxVQpxshiN76QUwblPu-ggj6fl594-ORiU","startTimeMs":"1891037","endTimeMs":"1906037"}
            Regex reg1 = new(@"clipConfig"":{""postId"":""(?:[\w-]+)"",""startTimeMs"":""(\d+)"",""endTimeMs"":""(\d+)""}");
            Match match1 = reg1.Match(body);
            if (double.TryParse(match1.Groups[1].Value, out double _start)
                && double.TryParse(match1.Groups[2].Value, out double _end))
            {
                source.start = _start / 1000;
                source.end = _end / 1000;
            }

            // {"videoId":"Gs7QYATahy4"}
            Regex reg2 = new(@"{""videoId"":""([\w-]+)""");
            Match match2 = reg2.Match(body);
            source.videoId = match2.Groups[1].Value;
            log.LogInformation("Get info from clip: {videoId}, {start}, {end}", source.videoId, source.start, source.end);
        }

        return clip;
    }

    private static async Task<string> ProcessAudioFileAsync(HttpRequest req, ILogger log, Source source)
    {
        IFormFileCollection files = req.Form.Files;
        log.LogInformation("Files Count: {fileCount}", files.Count);
        if (files.Count > 0)
        {
            return await ProcessAudioFromFileUpload(files, log);
        }
        // source檢核
        else if (string.IsNullOrEmpty(source.videoId)
                 || source.end - source.start <= 0
                 || source.end - source.start > 180)
        {
            log.LogError("video time invalid: {start}, {end}", source.start, source.end);
            throw new Exception($"video time invalid: {source.start}, {source.end}");
        }
        return "";
    }

    private static async Task<string> ProcessAudioFromFileUpload(IFormFileCollection files, ILogger log)
    {
        string tempPath = Path.Combine(_tempDir, DateTime.Now.Ticks.ToString() + ".tmp");

        log.LogInformation("Get file from form post.");

        // 有音檔，直接寫到暫存路徑使用
        IFormFile file = files[0];
        // Get file info
        var _fileExtension = Path.GetExtension(file.FileName) ?? "";
        tempPath = Path.ChangeExtension(tempPath, _fileExtension);
        log.LogInformation($"Get extension: {_fileExtension}");
        using (var fs = new FileStream(tempPath, FileMode.OpenOrCreate, FileAccess.Write))
        {
            file.CopyTo(fs);
            log.LogInformation("Write file from upload.");
        }

        return _fileExtension == ".webm" 
            ? tempPath 
            : await TranscodeAudioAsync(tempPath, log);
    }

    private static void CleanUp()
    {
        var extensions = new HashSet<string>() { ".mp3", ".ytdl", ".webm", ".exe", ".wav", "weba", ".flac" };
        new DirectoryInfo(_tempDir).GetFiles()
                                   .Where(p => extensions.Contains(p.Extension))
                                   .ToList()
                                   .ForEach(p => p.Delete());
    }

    [FunctionName("main-sound-buttons")]
    public static async Task<bool> RunOrchestrator(
        [OrchestrationTrigger] IDurableOrchestrationContext context,
        ILogger log)
    {
        Request request = context.GetInput<Request>();

        // 若表單有音檔，前一步驟會把檔案放入這個路徑
        if (string.IsNullOrEmpty(request.tempPath))
        {
            request.tempPath = await context.CallActivityAsync<string>("ProcessAudioAsync", request.source);
        }

        request = await context.CallActivityAsync<Request>("UploadAudioToStorageAsync", request);

        await context.CallActivityAsync("ProcessJsonFile", request);

        CleanUp();

        return true;
    }
}

