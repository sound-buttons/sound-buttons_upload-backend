using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace SoundButtons
{
    public class SoundButtons
    {
        [FunctionName("wake")]
        public static async Task<IActionResult> Wake([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
                                             ILogger log,
                                             [Blob("sound-buttons"), StorageAccount("AzureStorage")] BlobContainerClient BlobContainerClient)
            => await Task.Run(() => { return new OkResult(); });

        [FunctionName("cache-exists")]
        public static IActionResult CacheExists([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
                                         ILogger log,
                                         [Blob("sound-buttons"), StorageAccount("AzureStorage")] BlobContainerClient BlobContainerClient)
            => new OkObjectResult(req.Query.TryGetValue("id", out var videoId)
                                  && BlobContainerClient.GetBlobClient($"AudioSource/{videoId}").Exists());

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

            // 取得中文名稱做為檔名
            if (!req.Form.ContainsKey("nameZH"))
                return (ActionResult)new BadRequestResult();
            string name = req.Form.GetFirstValue("nameZH"); // 用於回傳
            string filename = name ?? "";
            filename = Regex.Replace(filename, @"[^0-9a-zA-Z\p{L}]+", ""); // 比對通過英數、中日文字等(多位元組字元)
            if (filename.Length == 0)
                filename = Guid.NewGuid().ToString("n");
            log.LogInformation("FileName: {filename}", filename);

            // 取得角色
            string directory = req.Form.GetFirstValue("directory") ?? "test";
            log.LogInformation($"Directory: {directory}");

            // 取得youtube影片id和秒數
            _ = int.TryParse(req.Form.GetFirstValue("start"), out int start);
            _ = int.TryParse(req.Form.GetFirstValue("end"), out int end);
            var source = new Source
            {
                videoId = req.Form.GetFirstValue("videoId") ?? "",
                start = start,
                end = end
            };

            if (source.videoId.StartsWith("http"))
            {
                // Regex for strip youtube video id from url c# and returl default thumbnail
                // https://gist.github.com/Flatlineato/f4cc3f3937272646d4b0
                source.videoId = Regex.Match(
                    source.videoId,
                    "https?:\\/\\/(?:[0-9A-Z-]+\\.)?(?:youtu\\.be\\/|youtube(?:-nocookie)?\\.com\\S*[^\\w\\s-])([\\w-]{11})(?=[^\\w-]|$)(?![?=&+%\\w.-]*(?:['\"][^<>]*>|<\\/a>))[?=&+%\\w.-]*",
                    RegexOptions.IgnoreCase).Groups[1].Value;

                if (string.IsNullOrEmpty(source.videoId))
                {
                    // Discard unknown source
                    source.videoId = "";
                    source.start = 0;
                    source.end = 0;
                }
            }

            // 處理剪輯片段
            string clip = req.Form.GetFirstValue("clip");
            if (!string.IsNullOrEmpty(clip))
            {
                using HttpClient client = new();
                var response = await client.GetAsync(clip);
                string body = await response.Content.ReadAsStringAsync();

                // "clipConfig":{"postId":"UgkxVQpxshiN76QUwblPu-ggj6fl594-ORiU","startTimeMs":"1891037","endTimeMs":"1906037"}
                Regex reg1 = new(@"clipConfig"":{""postId"":""([\w-]+)"",""startTimeMs"":""(\d+)"",""endTimeMs"":""(\d+)""}");
                Match match1 = reg1.Match(body);
                source.start = Convert.ToInt32(match1.Groups[2].Value) / 1000;
                source.end = Convert.ToInt32(match1.Groups[3].Value) / 1000;

                // {"videoId":"Gs7QYATahy4"}
                Regex reg2 = new(@"{""videoId"":""([\w-]+)""");
                Match match2 = reg2.Match(body);
                source.videoId = match2.Groups[1].Value;
            }
            log.LogInformation("{videoId}: {start}, {end}", source.videoId, source.start, source.end);

            // toast ID用於回傳，讓前端能取消顯示toast
            string toastId = req.Form.GetFirstValue("toastId") ?? "-1";

            string tempPath = "";
            IFormFileCollection files = req.Form.Files;
            log.LogInformation("Files Count: {fileCount}", files.Count);
            if (files.Count > 0)
            {
                tempPath = ProcessAudioWithFile(files, source, log);
            }
            // source檢核
            else if (string.IsNullOrEmpty(source.videoId)
                     || source.end - source.start <= 0
                     || source.end - source.start > 180)
            {
                return (ActionResult)new BadRequestResult();
            }

            string ip = req.Headers.FirstOrDefault(x => x.Key == "X-Forwarded-For").Value.FirstOrDefault();

            string nameZH = req.Form.GetFirstValue("nameZH") ?? "";
            string nameJP = req.Form.GetFirstValue("nameJP") ?? "";
            if (!float.TryParse(req.Form.GetFirstValue("volume"), out float volume)) { volume = 1; }
            string group = req.Form.GetFirstValue("group") ?? "未分類";

            // Function input comes from the request content.
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

        [FunctionName("main-sound-buttons")]
        public static async Task<bool> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log)
        {
            Request request = context.GetInput<Request>();
            if (string.IsNullOrEmpty(request.tempPath))
            {
                request.tempPath = await context.CallActivityAsync<string>("ProcessAudioAsync", request.source);
            }

            // Upload to Blob Storage
            request = await context.CallActivityAsync<Request>("UploadAudioToStorageAsync", request);

            await context.CallActivityAsync("ProcessJsonFile", request);

            return true;
        }

        private static string ProcessAudioWithFile(IFormFileCollection files, Source source, ILogger log)
        {
#if DEBUG
            string tempDir = Path.GetTempPath();
#else
            string tempDir = @"C:\home\data";
#endif
            string tempPath = Path.Combine(tempDir, DateTime.Now.Ticks.ToString() + ".tmp");

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
            return tempPath;
        }

        [FunctionName("ProcessAudioAsync")]
        public static async Task<string> ProcessAudioAsync(
            [ActivityTrigger] Source source,
            ILogger log,
            [Blob("sound-buttons"), StorageAccount("AzureStorage")] BlobContainerClient blobContainerClient)
        {
#if DEBUG
            string tempDir = Path.GetTempPath();
#else
            string tempDir = @"C:\home\data";
#endif
            string tempPath = Path.Combine(tempDir, DateTime.Now.Ticks.ToString() + ".tmp");

            log.LogInformation("TempDir: {tempDir}", tempDir);

            // 設定非同步更新FFmpeg的task
            FFmpeg.SetExecutablesPath(tempDir);
            Task task = FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, FFmpeg.ExecutablesPath);
            //log.LogInformation("FFmpeg Path: {ffmpegPath}", FFmpeg.ExecutablesPath);

            #region 由storage檢查音檔
            BlobClient sourceBlob = blobContainerClient.GetBlobClient($"AudioSource/{source.videoId}");
            if (sourceBlob.Exists())
            {
                log.LogInformation("Start to download audio source from blob storage {name}", sourceBlob.Name);
                string sourcePath = Path.Combine(tempDir, DateTime.Now.Ticks.ToString());
                try
                {
                    _ = sourceBlob.DownloadTo(sourcePath);

                    string contentType = sourceBlob.GetProperties().Value.ContentType;
                    if (contentType.ToLower() == "audio/webm")
                    {
                        File.Move(sourcePath, Path.ChangeExtension(sourcePath, "webm"));
                        sourcePath = Path.ChangeExtension(sourcePath, "webm");
                    }
                    else if (contentType.ToLower() == "video/mp4")
                    {
                        File.Move(sourcePath, Path.ChangeExtension(sourcePath, "m4a"));
                        sourcePath = Path.ChangeExtension(sourcePath, "m4a");
                    }

                    tempPath = await CutAudioAsync(sourcePath, tempPath, source, log);
                }
                finally { File.Delete(sourcePath); }

                return tempPath;
            }
            #endregion

            #region 由youtube下載音檔
            string youtubeDLPath = Path.Combine(tempDir, DateTime.Now.Ticks.ToString() + "yt-dlp.exe");
            try
            {
                try
                {
                    // 同步下載youtube-dl.exe (yt-dlp.exe)
                    HttpClient httpClient = new();
                    using HttpResponseMessage response = await httpClient.GetAsync(new Uri(@"https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe").ToString());
                    response.EnsureSuccessStatusCode();
                    using var ms = await response.Content.ReadAsStreamAsync();
                    using var fs = File.Create(youtubeDLPath);
                    ms.Seek(0, SeekOrigin.Begin);
                    await ms.CopyToAsync(fs);
                    await fs.FlushAsync();
                }
                catch (Exception)
                {
                    // Download failed fallback
                    if (File.Exists("yt-dlp.exe"))
                        File.Copy("yt-dlp.exe", youtubeDLPath, true);
                }
                log.LogInformation("Download youtube-dl.exe at {ytdlPath}", youtubeDLPath);

                OptionSet optionSet = new()
                {
                    // 最佳音質
                    Format = "251",
                    NoCheckCertificate = true,
                    Output = tempPath.Replace(".tmp", "_org.%(ext)s")
                };

                if (File.Exists("aria2c.exe"))
                {
                    File.Copy("aria2c.exe", Path.Combine(tempDir, "aria2c.exe"), true);
                    optionSet.ExternalDownloader = "aria2c";
                    optionSet.ExternalDownloaderArgs = "-j 16 -s 16 -x 16 -k 1M --retry-wait 10 --max-tries 10";
                }

                // 下載音訊來源
                log.LogInformation("Start to download audio source from youtube {videoId}", source.videoId);

                string sourcePath = string.Empty;
                YoutubeDLProcess youtubeDLProcess = new(youtubeDLPath);

                youtubeDLProcess.OutputReceived += (object sender, System.Diagnostics.DataReceivedEventArgs e) =>
                {
                    log.LogInformation(e.Data);

                    // 由console輸出中比對出檔名
                    Match match = new Regex("Destination: (.*)", RegexOptions.Compiled).Match(e.Data);
                    if (match.Success)
                    {
                        sourcePath = match.Groups[1].ToString().Trim();
                    }
                };
                youtubeDLProcess.ErrorReceived += (object sender, System.Diagnostics.DataReceivedEventArgs e)
                    => log.LogError(e.Data);

                await Task.WhenAll(task, youtubeDLProcess.RunAsync(
                    new string[] { @$"https://youtu.be/{source.videoId}" },
                    optionSet,
                    new System.Threading.CancellationToken())
                );

                if (!string.IsNullOrEmpty(sourcePath))
                {
                    try
                    {
                        tempPath = await CutAudioAsync(sourcePath, tempPath, source, log);
                    }
                    finally { File.Delete(sourcePath); }
                }
                else { throw new Exception("BadRequest"); }
                return tempPath;
            }
            finally { File.Delete(youtubeDLPath); }
            #endregion
        }

        private static async Task<string> CutAudioAsync(string sourcePath, string tempPath, Source source, ILogger log)
        {
            log.LogInformation("Downloaded audio: {sourcePath}", sourcePath);
            string fileExtension = Path.GetExtension(sourcePath);
            log.LogInformation("Get extension: {fileExtension}", fileExtension);
            tempPath = Path.ChangeExtension(tempPath, fileExtension);

            // 剪切音檔
            log.LogInformation("Start to cut audio");
            List<IStream> list = FFmpeg.GetMediaInfo(sourcePath)
                                       .GetAwaiter()
                                       .GetResult()
                                       .AudioStreams
                                       .Select(audioStream => audioStream.Split(startTime: TimeSpan.FromSeconds(source.start),
                                                                                duration: TimeSpan.FromSeconds(source.end - source.start))
                                               as IStream)
                                       .ToList();
            IConversion conversion = new Conversion().AddStream(list)
                                                     .SetOutput(tempPath);
            IConversionResult convRes = await conversion.Start();
            log.LogInformation("Cut audio Finish: {path}", tempPath);
            log.LogInformation("Cut audio Finish in {duration} seconds.", convRes.Duration.TotalSeconds);
            return tempPath;
        }

        [FunctionName("UploadAudioToStorageAsync")]
        public static async Task<Request> UploadAudioToStorageAsync(
            [ActivityTrigger] Request request,
            ILogger log,
            [Blob("sound-buttons"), StorageAccount("AzureStorage")] BlobContainerClient blobContainerClient)
        {
            string ip = request.ip;
            string filename = request.filename;
            string directory = request.directory;
            string tempPath = request.tempPath;
            string fileExtension = Path.GetExtension(tempPath);

            // Get a new file name on blob storage
            BlobClient cloudBlockBlob = blobContainerClient.GetBlobClient($"{directory}/{filename + fileExtension}");
            if (cloudBlockBlob.Exists())
            {
                filename += $"_{DateTime.Now.Ticks}";
                cloudBlockBlob = blobContainerClient.GetBlobClient($"{directory}/{filename + fileExtension}");
            }
            request.filename = filename;
            log.LogInformation($"Filename: {filename + fileExtension}");

            // Get a new SAS token for the file
            // Check whether this BlobClient object has been authorized with Shared Key.
            if (cloudBlockBlob.CanGenerateSasUri)
            {
                BlobSasBuilder sasBuilder = new()
                {
                    BlobContainerName = cloudBlockBlob.GetParentBlobContainerClient().Name,
                    BlobName = cloudBlockBlob.Name,
                    Resource = "b",
                    Identifier = "永讀"
                };

                Uri sasUri = cloudBlockBlob.GenerateSasUri(sasBuilder);
                log.LogInformation($"SAS URI for blob is: {sasUri}");

                request.sasContainerToken = sasUri.Query;
            }
            else
            {
                log.LogCritical(@"BlobClient must be authorized with Shared Key 
                          credentials to create a service SAS.");
            }

            try
            {
                // Write audio file 
                log.LogInformation("Start to upload audio to blob storage {name}", blobContainerClient.Name);
                await cloudBlockBlob.UploadAsync(tempPath, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "audio/basic" } });
                log.LogInformation("Upload audio to azure finish.");
            }
            finally { File.Delete(tempPath); }

            if (null != ip)
            {
                Dictionary<string, string> metadata = new()
                {
                    { "sourceIp", ip }
                };
                await cloudBlockBlob.SetMetadataAsync(metadata);
            }
            return request;
        }

        [FunctionName("ProcessJsonFile")]
        public static async Task ProcessJsonFile(
            [ActivityTrigger] Request request,
            ILogger log,
            [Blob("sound-buttons"), StorageAccount("AzureStorage")] BlobContainerClient BlobContainerClient)
        {
            Source source = request.source;
            string directory = request.directory;
            string filename = request.filename;
            string sasContainerToken = request.sasContainerToken;
            string fileExtension = Path.GetExtension(request.tempPath);
            // Get last json file
            BlobClient jsonBlob = BlobContainerClient.GetBlobClient($"{directory}/{directory}.json");
            if (!jsonBlob.Exists().Value)
            {
                log.LogCritical("{jsonFile} not found!!", jsonBlob.Name);
                return;
            }
            log.LogInformation("Read Json file {name}", jsonBlob.Name);

            JsonRoot root;
            // Read last json file
            using (MemoryStream ms = new())
            {
                try
                {
                    await jsonBlob.OpenRead().CopyToAsync(ms);
                }
                catch (OutOfMemoryException)
                {
                    log.LogError("System.OutOfMemoryException!! Directly try again.");
                    // Retry and let it fail if it comes up again.
                    await jsonBlob.OpenRead(new BlobOpenReadOptions(false)
                    {
                        BufferSize = 8192,
                        Position = 0
                    }).CopyToAsync(ms);
                }

                ms.Seek(0,SeekOrigin.Begin);
                root = await JsonSerializer.DeserializeAsync<JsonRoot>(ms, new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    // For Unicode and '&' characters
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            }

            // Get new json file block
            BlobClient newjsonBlob = BlobContainerClient.GetBlobClient($"{directory}/UploadJson/{DateTime.Now:yyyy-MM-dd-HH-mm}.json");

            // Generate new json file
            JsonRoot json = UpdateJson(root,
                                       directory,
                                       filename + fileExtension,
                                       request,
                                       source,
                                       sasContainerToken);
            byte[] result = JsonSerializer.SerializeToUtf8Bytes<JsonRoot>(
                json,
                new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = true
                });

            log.LogInformation("Write Json {name}", jsonBlob.Name);
            log.LogInformation("Write Json backup {name}", newjsonBlob.Name);

            // Write new json file
            var option = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" } };
            await Task.WhenAll(newjsonBlob.UploadAsync(new BinaryData(result), option),
                               jsonBlob.UploadAsync(new BinaryData(result), option));
        }

        private static JsonRoot UpdateJson(JsonRoot root, string directory, string filename, Request request, Source source, string SASToken)
        {
            // Variables prepare
            string baseRoute = $"https://soundbuttons.blob.core.windows.net/sound-buttons/{directory}/";

            string group = request.group;

            // Get ButtonGrop if exists, or new one
            ButtonGroup buttonGroup = null;
            foreach (var btg in root.buttonGroups)
            {
                try
                {
                    var name = btg.name.ZhTw;

                    if (group == name)
                    {
                        buttonGroup = btg;

                        break;
                    }
                }
                catch (InvalidCastException) { }
            }
            if (null == buttonGroup)
            {
                buttonGroup = new ButtonGroup
                {
                    name = new Text(group, group),
                    baseRoute = baseRoute,
                    buttons = new List<Button>()
                };
                root.buttonGroups.Add(buttonGroup);
            }

            // Prevent script injection
            source.videoId = System.Web.HttpUtility.UrlEncode(source.videoId);

            // Add button

            buttonGroup.buttons.Add(new Button(
                filename,
                new Text(
                    request.nameZH,
                    request.nameJP
                ),
                request.volume,
                source,
                SASToken
            ));

            return root;
        }

        #region POCO
#pragma warning disable IDE1006 // 命名樣式
        public class Request
        {
            public string ip { get; set; }
            public string filename { get; set; }
            public string directory { get; set; }
            public Source source { get; set; }
            public string clip { get; set; }
            public string nameZH { get; set; }
            public string nameJP { get; set; }
            public float volume { get; set; }
            public string group { get; set; }
            public string tempPath { get; set; }
            public string sasContainerToken { get; set; }
            public string toastId { get; set; }
        }

        public class Color
        {
            public string primary { get; set; }
            public string secondary { get; set; }

            public Color() { }
        }

        public class Link
        {
            public string youtube { get; set; }
            public string twitter { get; set; }
            public string facebook { get; set; }
            public string other { get; set; }

            public Link() { }
        }

        public class Text
        {
            [JsonPropertyName("zh-tw")]
            public string ZhTw { get; set; }
            public string ja { get; set; }

            public Text() { }

            public Text(string zhTw, string ja)
            {
                ZhTw = zhTw;
                this.ja = ja;
            }
        }

        public class IntroButton : Button
        {
        }

        public class Source
        {
            public string videoId { get; set; }
            public int start { get; set; }
            public int end { get; set; }

            public Source() { }

            public Source(string videoId, int start, int end)
            {
                this.videoId = videoId;
                this.start = start;
                this.end = end;
            }
        }

        public class Button
        {
            public string filename { get; set; }
            public object text { get; set; }
            public string baseRoute { get; set; }
            private float _volume;
            public float volume
            {
                get => _volume;
                set => _volume = value == 0
                                     ? 1
                                     : value;
            }
            public Source source { get; set; }
            public string SASToken { get; set; }

#pragma warning disable CA2245 // 請勿將屬性指派給屬性自身
            public Button() => this.volume = volume;
#pragma warning restore CA2245 // 請勿將屬性指派給屬性自身

            public Button(string filename, object text, float volume, Source source, string sASToken)
            {
                this.filename = filename;
                this.text = text;
                this.volume = volume;
                this.source = source;
                SASToken = sASToken;
            }
        }

        public class ButtonGroup
        {
            public Text name { get; set; }
            public string baseRoute { get; set; }
            public List<Button> buttons { get; set; }

            public ButtonGroup() { }
        }

        public class JsonRoot
        {
            public string name { get; set; }
            public string fullName { get; set; }
            public string fullConfigURL { get; set; }
            public string imgSrc { get; set; }
            public string intro { get; set; }
            public Color color { get; set; }
            public Link link { get; set; }
            public IntroButton introButton { get; set; }
            public List<ButtonGroup> buttonGroups { get; set; }

            public JsonRoot() { }
        }
#pragma warning restore IDE1006 // 命名樣式
        #endregion
    }

    static class Extension
    {
        internal static string GetFirstValue(this IFormCollection form, string name)
        {
            string result = null;
            if (form.TryGetValue(name, out var sv))
            {
                result = sv.FirstOrDefault();
            }
            return result;
        }
    }
}

