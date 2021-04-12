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

            if (contentType.Contains("multipart/form-data;"))
            {
                #region Audio file
                // Get audio file
                IFormFileCollection files = req.Form.Files;
                if (files.Count <= 0)
                {
                    return new BadRequestResult();
                }
                IFormFile file = files[0];

                // Get file info
                string filename = req.Form.GetFirstValue("nameZH") ?? Guid.NewGuid().ToString("n");
                filename = filename.Replace("\"", "").Replace(" ", "_");
                string directory = req.Form.GetFirstValue("directory") ?? "test";
                log.LogInformation($"Directory: {directory}");
                string fileExtension = Path.GetExtension(file.FileName) ?? "";
                log.LogInformation($"Get extension: {fileExtension}");

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
                cloudBlockBlob.Properties.ContentType = file.ContentType;
                cloudBlockBlob.Metadata.Add("origName", file.FileName);

                string ip = req.Headers.FirstOrDefault(x => x.Key == "X-Forwarded-For").Value.FirstOrDefault();
                if (null != ip)
                {
                    cloudBlockBlob.Metadata.Add("sourceIp", ip);
                }

                // Write audio file 
                using (var stream = file.OpenReadStream())
                {
                    await cloudBlockBlob.UploadFromStreamAsync(stream);
                }
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
                await newjsonBlob.UploadFromByteArrayAsync(result, 0, result.Length);
                await jsonBlob.UploadFromByteArrayAsync(result, 0, result.Length);
                #endregion

                return (ActionResult)new OkObjectResult(new { name = filename });
            }
            return (ActionResult)new BadRequestResult();
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

