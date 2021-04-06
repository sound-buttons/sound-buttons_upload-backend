using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace SoundButtons {
    public static class SoundButtons {
        private static ILogger logger;
        [FunctionName("sound-buttons")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
                                                    ILogger log,
                                                    [Blob("sound-buttons"), StorageAccount("AzureWebJobsStorage")] CloudBlobContainer cloudBlobContainer) {
            logger = log;
            string contentType = req.ContentType;
            log.LogInformation($"Content-Type: {contentType}");

            if (contentType.Contains("multipart/form-data;")) {
                #region Audio file
                // Get audio file
                IFormFileCollection files = req.Form.Files;
                if (files.Count <= 0) {
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
                if (cloudBlockBlob.Exists()) {
                    filename += $"_{DateTime.Now.Ticks}";
                    cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference($"{directory}/{filename + fileExtension}");
                }
                filename += fileExtension;
                log.LogInformation($"Filename: {filename}");

                // Get a new SAS token for the file
                string sasContainerToken = cloudBlockBlob.GetSharedAccessSignature(null, "¥ÃÅª");

                // Set info on the blob storage block
                cloudBlockBlob.Properties.ContentType = file.ContentType;
                cloudBlockBlob.Metadata.Add("origName", file.FileName);

                string ip = req.Headers.FirstOrDefault(x => x.Key == "X-Forwarded-For").Value.FirstOrDefault();
                if (null != ip) {
                    cloudBlockBlob.Metadata.Add("sourceIp", ip);
                }

                // Write audio file 
                using (var stream = file.OpenReadStream()) {
                    await cloudBlockBlob.UploadFromStreamAsync(stream);
                }
                #endregion

                #region Json File
                // Get last json file
                string lastJsonUri = cloudBlobContainer.ListBlobs($"{directory}/UploadJson", true).LastOrDefault()?.Uri.AbsolutePath;
                if (string.IsNullOrEmpty(lastJsonUri)) {
                    return (ActionResult)new InternalServerErrorResult();
                }
                // Change : /sound-buttons/tama/UploadJson/2021-04-05-22-49.json
                // to     : tama/UploadJson/2021-04-05-22-49.json
                lastJsonUri = lastJsonUri[(lastJsonUri.IndexOf("/") + 1)..];
                lastJsonUri = lastJsonUri[(lastJsonUri.IndexOf("/") + 1)..];
                log.LogInformation($"Last json file: {lastJsonUri}");
                CloudBlockBlob jsonBlob = cloudBlobContainer.GetBlockBlobReference(lastJsonUri);

                // Read last json file
                using (Stream input = jsonBlob.OpenRead()) {
                    JsonRoot root = await JsonSerializer.DeserializeAsync<JsonRoot>(input, new JsonSerializerOptions {
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true,
                        // For Unicode and '&' characters
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });

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
                        new JsonSerializerOptions {
                            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                            WriteIndented = true
                        });

                    // Write new json file
                    await newjsonBlob.UploadFromByteArrayAsync(result, 0, result.Length);
                }
                #endregion

                return (ActionResult)new OkObjectResult(new { name = filename });
            }
            return (ActionResult)new BadRequestResult();
        }

        private static string GetFirstValue(this IFormCollection form, string name) {
            string result = null;
            if (form.TryGetValue(name, out var sv) && sv.Count > 0 && !string.IsNullOrEmpty(sv[0])) {
                result = sv[0];
            }
            return result;
        }

        private static JsonRoot UpdateJson(JsonRoot root, string directory, string filename, IFormCollection form, string SASToken) {
            string baseRoute = "https://jim60105.blob.core.windows.net/sound-buttons/" + directory;
            string group = form.GetFirstValue("group") ?? "default";

            ButtonGroup buttonGroup = null;
            foreach (var btg in root.buttonGroups) {
                try {
                    var name = btg.name.ZhTw;

                    if (group == name) {
                        buttonGroup = btg;

                        break;
                    }
                } catch (InvalidCastException) { }
            }
            if (null == buttonGroup) {
                buttonGroup = new ButtonGroup();
                buttonGroup.name = new Text(group, group);
                buttonGroup.baseRoute = baseRoute;
                buttonGroup.buttons = new List<Button>();
                root.buttonGroups.Add(buttonGroup);
            }

            buttonGroup.buttons.Add(new Button(
                filename,
                new Text(
                    form.GetFirstValue("nameZH") ?? "",
                    form.GetFirstValue("nameJP") ?? ""
                ),
                SASToken
            ));

            return root;
        }

        #region POCO
        public class Color {
            public string primary { get; set; }
            public string secondary { get; set; }
        }

        public class Link {
            public string youtube { get; set; }
            public string twitter { get; set; }
            public string facebook { get; set; }
            public string other { get; set; }
        }

        public class Text {
            [JsonPropertyName("zh-tw")]
            public string ZhTw { get; set; }
            public string ja { get; set; }

            public Text() { }

            public Text(string zhTw, string ja) {
                ZhTw = zhTw;
                this.ja = ja;
            }
        }

        public class IntroButton : Button {
        }

        public class Button {
            public string filename { get; set; }
            public object text { get; set; }
            public string baseRoute { get; set; }
            public string source { get; set; }
            public string SASToken { get; set; }

            public Button() { }

            public Button(string filename, object text, string sASToken) {
                this.filename = filename;
                this.text = text;
                SASToken = sASToken;
            }
        }

        public class ButtonGroup {
            public Text name { get; set; }
            public string baseRoute { get; set; }
            public List<Button> buttons { get; set; }

            public ButtonGroup() { }
        }

        public class JsonRoot {
            public string name { get; set; }
            public string fullName { get; set; }
            public string fullConfigURL { get; set; }
            public string imgSrc { get; set; }
            public string intro { get; set; }
            public Color color { get; set; }
            public Link link { get; set; }
            public IntroButton introButton { get; set; }
            public List<ButtonGroup> buttonGroups { get; set; }
        }
        #endregion
    }
}

