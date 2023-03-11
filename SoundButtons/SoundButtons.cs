using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using SoundButtons.Models;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SoundButtons;

public partial class SoundButtons
{
    private readonly ILogger _logger;
    internal string _tempDir = @"C:\home\data\SoundButtons";

    public SoundButtons()
    {
#if DEBUG
        Serilog.Debugging.SelfLog.Enable(msg => Console.WriteLine(msg));
#endif

        _logger = new LoggerConfiguration()
                        .MinimumLevel.Verbose()
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Fatal)
                        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Fatal)
                        .MinimumLevel.Override("System", LogEventLevel.Fatal)
                        .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj} <{SourceContext}>{NewLine}{Exception}",
                                         restrictedToMinimumLevel: LogEventLevel.Verbose)
                        .WriteTo.Seq(serverUrl: Environment.GetEnvironmentVariable("Seq_ServerUrl"),
                                     apiKey: Environment.GetEnvironmentVariable("Seq_ApiKey"),
                                     restrictedToMinimumLevel: LogEventLevel.Verbose)
                        .Enrich.FromLogContext()
                        .CreateLogger();

        //_logger.Debug("Starting up...");

        PrepareTempDir();
    }

    public void PrepareTempDir()
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
    public async Task<IActionResult> HttpStart(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
        [DurableClient] IDurableOrchestrationClient starter)
    {
        // 驗證ContentType為multipart/form-data
        string contentType = req.ContentType;
        _logger.Information($"Content-Type: {contentType}");
        if (!contentType.Contains("multipart/form-data;"))
            return (ActionResult)new BadRequestResult();

        if (!req.Form.ContainsKey("nameZH"))
            return (ActionResult)new BadRequestResult();

        Source source = GetSourceInfo(req);
        string clip = await ProcessYoutubeClip(req, source);

        string tempPath = "";
        try
        {
            tempPath = await ProcessAudioFileAsync(req, source);
        }
        catch (Exception e)
        {
            _logger.Error("ProcessAudioFileAsync: {exception}, {message}, {stacktrace}", e, e.Message, e.StackTrace);
            return (ActionResult)new BadRequestResult();
        }

        if (!float.TryParse(req.Form.GetFirstValue("volume"), out float volume)) { volume = 1; }

        // 啟動長輪詢
        IActionResult orchestratorResult = await StartOrchestrator(req: req,
                                                                   starter: starter,
                                                                   filename: GetFileName(req),
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

    private async Task<IActionResult> StartOrchestrator(HttpRequest req, IDurableOrchestrationClient starter, string filename, string directory, Source source, string clip, string toastId, string tempPath, string ip, string nameZH, string nameJP, float volume, string group)
    {
        string instanceId = Guid.NewGuid().ToString();
        await starter.StartNewAsync(
            orchestratorFunctionName: "main-sound-buttons",
            instanceId: instanceId,
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
                clip = clip,
                instanceId = instanceId
            });

        _logger.Information($"Started orchestration with ID = '{instanceId}'.");

        return starter.CreateCheckStatusResponse(req, instanceId, true);
    }

    private string GetFileName(HttpRequest req)
    {
        string name = req.Form.GetFirstValue("nameZH"); // 用於回傳
        string filename = name ?? "";
        filename = Regex.Replace(filename, @"[^0-9a-zA-Z\p{L}]+", ""); // 比對通過英數、中日文字等(多位元組字元)
        if (filename.Length == 0)
            filename = Guid.NewGuid().ToString("n");
        _logger.Information("FileName: {filename}", filename);
        return filename;
    }

    private Source GetSourceInfo(HttpRequest req)
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
                _logger.Error("Discard unknown source: {source}", source.videoId);
            }
            _logger.Information("Get info from form: {videoId}, {start}, {end}", source.videoId, source.start, source.end);
        }

        return source;
    }

    private async Task<string> ProcessYoutubeClip(HttpRequest req, Source source)
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
            _logger.Information("Get info from clip: {videoId}, {start}, {end}", source.videoId, source.start, source.end);
        }

        return clip;
    }

    private async Task<string> ProcessAudioFileAsync(HttpRequest req, Source source)
    {
        IFormFileCollection files = req.Form.Files;
        _logger.Information("Files Count: {fileCount}", files.Count);
        if (files.Count > 0)
        {
            return await ProcessAudioFromFileUpload(files);
        }
        // source檢核
        else if (string.IsNullOrEmpty(source.videoId)
                 || source.end - source.start <= 0
                 || source.end - source.start > 180)
        {
            _logger.Error("video time invalid: {start}, {end}", source.start, source.end);
            throw new Exception($"video time invalid: {source.start}, {source.end}");
        }
        return "";
    }

    private async Task<string> ProcessAudioFromFileUpload(IFormFileCollection files)
    {
        string tempPath = Path.Combine(_tempDir, DateTime.Now.Ticks.ToString() + ".tmp");

        _logger.Information("Get file from form post.");

        // 有音檔，直接寫到暫存路徑使用
        IFormFile file = files[0];
        // Get file info
        var _fileExtension = Path.GetExtension(file.FileName) ?? "";
        tempPath = Path.ChangeExtension(tempPath, _fileExtension);
        _logger.Information($"Get extension: {_fileExtension}");
        using (var fs = new FileStream(tempPath, FileMode.OpenOrCreate, FileAccess.Write))
        {
            file.CopyTo(fs);
            _logger.Information("Write file from upload.");
        }

        return _fileExtension == ".webm"
            ? tempPath
            : await TranscodeAudioAsync(tempPath);
    }

    private void CleanUp()
    {
        var extensions = new HashSet<string>() { ".mp3", ".ytdl", ".webm", ".exe", ".wav", "weba", ".flac" };
        new DirectoryInfo(_tempDir).GetFiles()
                                   .Where(p => extensions.Contains(p.Extension))
                                   .ToList()
                                   .ForEach(p => p.Delete());
    }

    [FunctionName("main-sound-buttons")]
    public async Task<bool> RunOrchestrator(
        [OrchestrationTrigger] IDurableOrchestrationContext context)
    {
        Request request = context.GetInput<Request>();
        using var _ = LogContext.PushProperty("InstanceId", request.instanceId);

        // 若表單有音檔，前一步驟會把檔案放入這個路徑
        if (string.IsNullOrEmpty(request.tempPath))
        {
            request.tempPath = await context.CallActivityAsync<string>("ProcessAudioAsync", request);
        }

        request = await context.CallActivityAsync<Request>("UploadAudioToStorageAsync", request);

        await context.CallActivityAsync("ProcessJsonFile", request);

        CleanUp();
        _logger.Information("Finish. {filename}", request.nameZH);

        return true;
    }
}

