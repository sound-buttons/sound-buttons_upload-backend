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
    public static class SoundButtons
    {
        [FunctionName("sound-buttons")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
                                                    ILogger log,
                                                    [Blob("sound-buttons"), StorageAccount("AzureWebJobsStorage")] CloudBlobContainer cloudBlobContainer)
        {
            string contentType = req.ContentType;
            log.LogInformation($"Content-Type: {contentType}");

            string filename, fileExtension = "";
            filename = req.Form.GetFirstValue("nameZH") ?? Guid.NewGuid().ToString("n");
            filename = filename.Replace("\"", "").Replace(" ", "_");
            string origFileName = filename;

            log.LogInformation("FileName: {filename}", filename);

            string directory = req.Form.GetFirstValue("directory") ?? "test";
            log.LogInformation($"Directory: {directory}");

            string videoId = req.Form.GetFirstValue("videoId");
            int.TryParse(req.Form.GetFirstValue("start"), out int start);
            int.TryParse(req.Form.GetFirstValue("end"), out int end);

#if DEBUG
            string tempDir = Path.GetTempPath();
#else
            string tempDir = @"C:\home\data";
#endif
            string tempPath = Path.Combine(tempDir, DateTime.Now.Ticks.ToString() + ".tmp");

            if (!contentType.Contains("multipart/form-data;"))
                return (ActionResult)new BadRequestResult();

            #region Process audio file
            // Get audio file
            IFormFileCollection files = req.Form.Files;
            log.LogInformation("Files Count: {fileCount}", files.Count);
            log.LogInformation("{videoId}: {start}, {end}", videoId, start, end);
            if (files.Count > 0)
            {
                IFormFile file = files[0];
                // Get file info
                fileExtension = Path.GetExtension(file.FileName) ?? "";
                tempPath = Path.ChangeExtension(tempPath, fileExtension);
                log.LogInformation($"Get extension: {fileExtension}");
                origFileName = file.FileName;
                using (var fs = new FileStream(tempPath, FileMode.OpenOrCreate, FileAccess.Write))
                {
                    file.CopyTo(fs);
                    log.LogInformation("Write file from upload.");
                }
            }
            else if (!string.IsNullOrEmpty(videoId) && end - start > 0)
            {
                //var ytdl = new YoutubeDL();
                log.LogInformation("TempDir: {tempDir}", tempDir);

                FFmpeg.SetExecutablesPath(tempDir);
                Task task = FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, FFmpeg.ExecutablesPath);
                log.LogInformation("FFmpeg Path: {ffmpegPath}", FFmpeg.ExecutablesPath);

                string youtubeDLPath = Path.Combine(tempDir, "youtube-dl.exe");
                try
                {
                    var wc = new System.Net.WebClient();
                    wc.DownloadFile(new Uri(@"https://github.com/blackjack4494/yt-dlc/releases/latest/download/youtube-dlc.exe"), youtubeDLPath);
                    log.LogInformation("Download youtube-dl.exe at {ytdlPath}", youtubeDLPath);

                    log.LogInformation("Start to download audio from {videoId}", videoId);

                    OptionSet optionSet = new OptionSet
                    {
                        Format = "140/m4a/bestaudio",
                        NoCheckCertificate = true,
                        Output = tempPath.Replace(".tmp", "_org.%(ext)s")
                    };

                    string source = string.Empty;
                    YoutubeDLProcess youtubeDLProcess = new YoutubeDLProcess(youtubeDLPath);

                    youtubeDLProcess.OutputReceived += (object sender, System.Diagnostics.DataReceivedEventArgs e) =>
                    {
                        log.LogInformation(e.Data);

                        Match match = new Regex("Destination: (.*)", RegexOptions.Compiled).Match(e.Data);
                        if (match.Success)
                        {
                            source = match.Groups[1].ToString().Trim();
                        }
                    };
                    youtubeDLProcess.ErrorReceived += (object sender, System.Diagnostics.DataReceivedEventArgs e)
                        => log.LogError(e.Data);

                    int res = await youtubeDLProcess.RunAsync(
                        new string[] { @$"https://youtu.be/{videoId}" },
                        optionSet,
                        new System.Threading.CancellationToken());

                    if (res == 0 && !string.IsNullOrEmpty(source))
                    {
                        try
                        {
                            log.LogInformation("Downloaded audio: {sourcePath}", source);
                            fileExtension = Path.GetExtension(source);
                            log.LogInformation("Get extension: {fileExtension}", fileExtension);
                            tempPath = Path.ChangeExtension(tempPath, fileExtension);

                            task.Wait();
                            log.LogInformation("Start to cut audio");
                            IConversion conversion = await FFmpeg.Conversions.FromSnippet.Split(source, tempPath, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end - start));
                            IConversionResult convRes = await conversion.Start();
                            log.LogInformation("Cut audio Finish: {path}", tempPath);
                            log.LogInformation("Cut audio Finish in {duration} seconds.", convRes.Duration.TotalSeconds);

                            origFileName = $"{videoId}_{start}_{end}{fileExtension}";
                        } finally {
                            File.Delete(source);
                        }
                    }
                    else { return (ActionResult)new BadRequestResult(); }
                } finally { File.Delete(youtubeDLPath); }
            }
            else { return (ActionResult)new BadRequestResult(); }

            #endregion

            #region Upload to Blob Storage
            // Get a new file name on blob storage
            CloudBlockBlob cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference($"{directory}/{filename + fileExtension}");
            if (cloudBlockBlob.Exists())
            {
                filename += $"_{DateTime.Now.Ticks}";
                cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference($"{directory}/{filename + fileExtension}");
            }
            filename += fileExtension;
            log.LogInformation($"Filename: {filename}");

            // Get a new SAS token for the file
            string sasContainerToken = cloudBlockBlob.GetSharedAccessSignature(null, "永讀");

            // Set info on the blob storage block
            cloudBlockBlob.Properties.ContentType = "audio/basic";
            cloudBlockBlob.Metadata.Add("origName", origFileName);

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

            #endregion

            #region Json File
            // Get last json file
            CloudBlockBlob jsonBlob = cloudBlobContainer.GetBlockBlobReference($"{directory}/{directory}.json");

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
            var result = JsonSerializer.SerializeToUtf8Bytes<JsonRoot>(
                UpdateJson(root,
                    directory,
                    filename,
                    req.Form,
                    sasContainerToken),
                new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = true
                });

            // Write new json file
            List<Task> tasks = new List<Task>();
            tasks.Add(newjsonBlob.UploadFromByteArrayAsync(result, 0, result.Length));
            tasks.Add(jsonBlob.UploadFromByteArrayAsync(result, 0, result.Length));
            Task.WaitAll(tasks.ToArray());
            #endregion

            return (ActionResult)new OkObjectResult(new { name = filename });
        }

        private static string GetFirstValue(this IFormCollection form, string name)
        {
            string result = null;
            if (form.TryGetValue(name, out var sv) && sv.Count > 0 && !string.IsNullOrEmpty(sv[0]))
            {
                result = sv[0];
            }
            return result;
        }

        private static JsonRoot UpdateJson(JsonRoot root, string directory, string filename, IFormCollection form, string SASToken)
        {
            // Variables prepare
            string baseRoute = $"https://jim60105.blob.core.windows.net/sound-buttons/{directory}/";

            string group = form.GetFirstValue("group") ?? "未分類";
            _ = int.TryParse(form.GetFirstValue("start"), out int start);
            _ = double.TryParse(form.GetFirstValue("end"), out double end);
            end = Math.Ceiling(end);
            string videoId = form.GetFirstValue("videoId") ?? "";
            if (videoId.StartsWith("https://youtu.be/"))
            {
                videoId = Regex.Match(videoId, "^.*/([^?]*).*$").Value;
            }
            else if (videoId.StartsWith("https://www.youtube.com/watch"))
            {
                videoId = Regex.Match(videoId, "^.*[?&]v=([^&]*).*$").Value;
            }

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

            // Add button
            buttonGroup.buttons.Add(new Button(
                filename,
                new Text(
                    form.GetFirstValue("nameZH") ?? "",
                    form.GetFirstValue("nameJP") ?? ""
                ),
                new Source(
                    System.Web.HttpUtility.UrlEncode(videoId),  // Prevent script injection
                    start,
                    (int)end
                ),
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
            public Source source { get; set; }
            public string SASToken { get; set; }

            public Button() { }

            public Button(string filename, object text, Source source, string sASToken)
            {
                this.filename = filename;
                this.text = text;
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
}

