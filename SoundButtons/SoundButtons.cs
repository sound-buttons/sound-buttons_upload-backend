using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public async Task<IActionResult> Wake([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
                                             ILogger log,
                                             [Blob("sound-buttons"), StorageAccount("AzureStorage")] CloudBlobContainer cloudBlobContainer)
            => await Task.Run(() => { return new OkResult(); });

        [FunctionName("cache-exists")]
        public IActionResult CacheExists([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
                                         ILogger log,
                                         [Blob("sound-buttons"), StorageAccount("AzureStorage")] CloudBlobContainer cloudBlobContainer)
            => new OkObjectResult(req.Query.TryGetValue("id", out var videoId)
                                  && cloudBlobContainer.GetBlockBlobReference($"AudioSource/{videoId}").Exists());

        [FunctionName("sound-buttons")]
        public async Task<IActionResult> HttpStart(
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
                    toastId = toastId
                });

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId, true);
        }

        [FunctionName("main-sound-buttons")]
        public async Task<bool> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log,
            [Blob("sound-buttons"), StorageAccount("AzureStorage")] CloudBlobContainer cloudBlobContainer)
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

        private string ProcessAudioWithFile(IFormFileCollection files, Source source, ILogger log)
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
            [Blob("sound-buttons"), StorageAccount("AzureStorage")] CloudBlobContainer cloudBlobContainer)
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
            CloudBlockBlob sourceBlob = cloudBlobContainer.GetBlockBlobReference($"AudioSource/{source.videoId}");
            if (sourceBlob.Exists())
            {
                log.LogInformation("Start to download audio source from blob storage {name}", sourceBlob.Name);
                string sourcePath = Path.Combine(tempDir, DateTime.Now.Ticks.ToString());
                try
                {
                    using (var fs = new FileStream(sourcePath, FileMode.OpenOrCreate, FileAccess.Write))
                    {
                        Task.WaitAll(task, Task.Run(() =>
                        {
                            return sourceBlob.DownloadToStreamAsync(fs);
                        }));
                    }

                    if (sourceBlob.Properties.ContentType.ToLower() == "audio/webm")
                    {
                        File.Move(sourcePath, Path.ChangeExtension(sourcePath, "webm"));
                        sourcePath = Path.ChangeExtension(sourcePath, "webm");
                    }
                    else if (sourceBlob.Properties.ContentType.ToLower() == "video/mp4")
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
            string youtubeDLPath = Path.Combine(tempDir, DateTime.Now.Ticks.ToString() + "youtube-dl.exe");
            try
            {
                try
                {
                    // 同步下載youtube-dl.exe (youtube-dlc)
                    var wc = new System.Net.WebClient();
                    wc.DownloadFile(new Uri(@"https://github.com/blackjack4494/yt-dlc/releases/latest/download/youtube-dlc.exe"), youtubeDLPath);
                }
                catch (System.Net.WebException)
                {
                    // WebException fallback
                    if (File.Exists("youtube-dlc.exe"))
                        File.Copy("youtube-dlc.exe", youtubeDLPath, true);
                }
                log.LogInformation("Download youtube-dl.exe at {ytdlPath}", youtubeDLPath);

                OptionSet optionSet = new OptionSet
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
                YoutubeDLProcess youtubeDLProcess = new YoutubeDLProcess(youtubeDLPath);

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

                Task.WaitAll(task, youtubeDLProcess.RunAsync(
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
            [Blob("sound-buttons"), StorageAccount("AzureStorage")] CloudBlobContainer cloudBlobContainer)
        {
            string ip = request.ip;
            string filename = request.filename;
            string directory = request.directory;
            string tempPath = request.tempPath;
            string fileExtension = Path.GetExtension(tempPath);

            // Get a new file name on blob storage
            CloudBlockBlob cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference($"{directory}/{filename + fileExtension}");
            if (cloudBlockBlob.Exists())
            {
                filename += $"_{DateTime.Now.Ticks}";
                cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference($"{directory}/{filename + fileExtension}");
            }
            log.LogInformation($"Filename: {filename + fileExtension}");
            log.LogInformation("Start to upload audio to blob storage {name}", cloudBlobContainer.Name);

            // Get a new SAS token for the file
            request.sasContainerToken = cloudBlockBlob.GetSharedAccessSignature(null, "永讀");

            // Set info on the blob storage block
            cloudBlockBlob.Properties.ContentType = "audio/basic";

            if (null != ip)
            {
                cloudBlockBlob.Metadata.Add("sourceIp", ip);
            }

            try
            {
                // Write audio file 
                using (var fs = new FileStream(tempPath, FileMode.Open))
                {
                    await cloudBlockBlob.UploadFromStreamAsync(fs);
                }
                log.LogInformation("Upload audio to azure finish.");
            }
            finally { File.Delete(tempPath); }
            return request;
        }

        [FunctionName("ProcessJsonFile")]
        public static async Task ProcessJsonFile(
            [ActivityTrigger] Request request,
            ILogger log,
            [Blob("sound-buttons"), StorageAccount("AzureStorage")] CloudBlobContainer cloudBlobContainer)
        {
            Source source = request.source;
            string directory = request.directory;
            string filename = request.filename;
            string sasContainerToken = request.sasContainerToken;
            string fileExtension = Path.GetExtension(request.tempPath);
            // Get last json file
            CloudBlockBlob jsonBlob = cloudBlobContainer.GetBlockBlobReference($"{directory}/{directory}.json");
            log.LogInformation("Read Json file {name}", jsonBlob.Name);

            JsonRoot root;
            // Read last json file
            using (Stream input = jsonBlob.OpenRead())
            {
                root = await JsonSerializer.DeserializeAsync<JsonRoot>(input, new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    // For Unicode and '&' characters
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            }

            // Get new json file block
            CloudBlockBlob newjsonBlob = cloudBlobContainer.GetBlockBlobReference($"{directory}/UploadJson/{DateTime.Now:yyyy-MM-dd-HH-mm}.json");

            newjsonBlob.Properties.ContentType = "application/json";

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
            Task.WaitAll(newjsonBlob.UploadFromByteArrayAsync(result, 0, result.Length),
                         jsonBlob.UploadFromByteArrayAsync(result, 0, result.Length));
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

            public Button()
            {
                this.volume = volume;
            }

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

