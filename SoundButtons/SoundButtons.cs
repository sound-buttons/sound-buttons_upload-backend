using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace SoundButtons
{
    public class SoundButtons
    {
        ILogger log;
        CloudBlobContainer cloudBlobContainer;
        [FunctionName("sound-buttons")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
                                             ILogger log,
                                             [Blob("sound-buttons"), StorageAccount("AzureStorage")] CloudBlobContainer cloudBlobContainer)
        {
            this.log = log;
            this.cloudBlobContainer = cloudBlobContainer;

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

            string tempPath;
            try
            { tempPath = await ProcessAudioAsync(req, source, cloudBlobContainer); }
            catch (Exception e)
            {
                if (e.Message == "BadRequest")
                {
                    return (ActionResult)new BadRequestObjectResult(new string[] { name, toastId });
                }
                else { throw e; }
            }
            string fileExtension = Path.GetExtension(tempPath);

            // Upload to Blob Storage
            string sasContainerToken = await UploadAudioToStorageAsync(req, filename, directory, tempPath);

            await ProcessJsonFile(req, source, directory, filename, fileExtension, sasContainerToken);

            return (ActionResult)new OkObjectResult(new string[] { name, toastId });
        }

        private async Task<string> ProcessAudioAsync(HttpRequest req, Source source, CloudBlobContainer cloudBlobContainer)
        {
#if DEBUG
            string tempDir = Path.GetTempPath();
#else
            string tempDir = @"C:\home\data";
#endif
            string tempPath = Path.Combine(tempDir, DateTime.Now.Ticks.ToString() + ".tmp");

            #region 由上傳取得音檔
            IFormFileCollection files = req.Form.Files;
            log.LogInformation("Files Count: {fileCount}", files.Count);
            if (files.Count > 0)
            {
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
            #endregion

            // source檢核
            if (string.IsNullOrEmpty(source.videoId)
                || source.end - source.start <= 0
                || source.end - source.start > 60)
            { throw new Exception("BadRequest"); }

            log.LogInformation("TempDir: {tempDir}", tempDir);

            // 設定非同步更新FFmpeg的task
            FFmpeg.SetExecutablesPath(tempDir);
            Task task = FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, FFmpeg.ExecutablesPath);
            //log.LogInformation("FFmpeg Path: {ffmpegPath}", FFmpeg.ExecutablesPath);

            #region 由storage檢查音檔
            CloudBlockBlob sourceBlob = cloudBlobContainer.GetBlockBlobReference($"AudioSource/{source.videoId}");
            if (sourceBlob.Exists() && sourceBlob.Metadata.TryGetValue("Extension", out string ext))
            {
                log.LogInformation("Start to download audio source from blob storage {name}", sourceBlob.Name);
                string sourcePath = Path.Combine(tempDir, DateTime.Now.Ticks.ToString());
                sourcePath = Path.ChangeExtension(sourcePath, ext);
                try
                {
                    using (var fs = new FileStream(sourcePath, FileMode.OpenOrCreate, FileAccess.Write))
                    {
                        Task.WaitAll(task, Task.Run(() =>
                        {
                            return sourceBlob.DownloadToStreamAsync(fs);
                        }));
                    }

                    tempPath = await CutAudioAsync(sourcePath, tempPath, source);
                } finally { File.Delete(sourcePath); }

                return tempPath;
            }
            #endregion

            #region 由youtube下載音檔
            string youtubeDLPath = Path.Combine(tempDir, DateTime.Now.Ticks.ToString() + "youtube-dl.exe");
            try
            {
                // 同步下載youtube-dl.exe (youtube-dlc)
                var wc = new System.Net.WebClient();
                wc.DownloadFile(new Uri(@"https://github.com/blackjack4494/yt-dlc/releases/latest/download/youtube-dlc.exe"), youtubeDLPath);
                log.LogInformation("Download youtube-dl.exe at {ytdlPath}", youtubeDLPath);

                // 下載音訊來源
                log.LogInformation("Start to download audio source from youtube {videoId}", source.videoId);

                OptionSet optionSet = new OptionSet
                {
                    // 最佳音質
                    Format = "140/m4a/bestaudio",
                    NoCheckCertificate = true,
                    Output = tempPath.Replace(".tmp", "_org.%(ext)s")
                };

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
                        tempPath = await CutAudioAsync(sourcePath, tempPath, source);
                    } finally { File.Delete(sourcePath); }
                }
                else { throw new Exception("BadRequest"); }
                return tempPath;
            } finally { File.Delete(youtubeDLPath); }
            #endregion
        }

        private async Task<string> CutAudioAsync(string sourcePath, string tempPath, Source source)
        {
            log.LogInformation("Downloaded audio: {sourcePath}", sourcePath);
            var fileExtension = Path.GetExtension(sourcePath);
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

        private async Task<string> UploadAudioToStorageAsync(HttpRequest req, string filename, string directory, string tempPath)
        {
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
            string sasContainerToken = cloudBlockBlob.GetSharedAccessSignature(null, "永讀");

            // Set info on the blob storage block
            cloudBlockBlob.Properties.ContentType = "audio/basic";

            string ip = req.Headers.FirstOrDefault(x => x.Key == "X-Forwarded-For").Value.FirstOrDefault();
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
            } finally { File.Delete(tempPath); }
            return sasContainerToken;
        }

        private async Task ProcessJsonFile(HttpRequest req, Source source, string directory, string filename, string fileExtension, string sasContainerToken)
        {
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
                                       req.Form,
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

        private JsonRoot UpdateJson(JsonRoot root, string directory, string filename, IFormCollection form, Source source, string SASToken)
        {
            // Variables prepare
            string baseRoute = $"https://soundbuttons.blob.core.windows.net/sound-buttons/{directory}/";

            string group = form.GetFirstValue("group") ?? "未分類";

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

            if (!float.TryParse(form.GetFirstValue("volume"), out float volume))
            { volume = 1; }

            buttonGroup.buttons.Add(new Button(
                filename,
                new Text(
                    form.GetFirstValue("nameZH") ?? "",
                    form.GetFirstValue("nameJP") ?? ""
                ),
                volume,
                source,
                SASToken
            ));

            return root;
        }

        #region POCO
#pragma warning disable IDE1006 // 命名樣式
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
            public float volume { get; set; }
            public Source source { get; set; }
            public string SASToken { get; set; }

            public Button() { }

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

