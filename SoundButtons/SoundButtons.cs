using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace SoundButtons {
    public static class SoundButtons {
        [FunctionName("sound-buttons")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
                                                    ILogger log,
                                                    [Blob("sound-buttons"), StorageAccount("AzureWebJobsStorage")] CloudBlobContainer cloudBlobContainer) {
            string contentType = req.ContentType;
            log.LogInformation($"Content-Type: {contentType}");

            if (contentType.Contains("multipart/form-data")) {
                IFormFileCollection files = req.Form.Files;
                if (files.Count<=0) {
                    return new BadRequestResult();
                }
                IFormFile file = files[0];

                string filename = req.Form.GetFirstValue("name") ?? Guid.NewGuid().ToString("n");
                filename = filename.Replace("\"", "").Replace(" ", "_");
                string directory = req.Form.GetFirstValue("directory") ?? "test";
                log.LogInformation($"Directory: {directory}");
                string fileExtension = Path.GetExtension(file.FileName) ?? "";
                log.LogInformation($"Get extension: {fileExtension}");

                CloudBlockBlob cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference($"{directory}/{filename + fileExtension}");
                if (cloudBlockBlob.Exists()) {
                    filename += $"_{DateTime.Now.Ticks}";
                    cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference($"{directory}/{filename + fileExtension}");
                }

                filename += fileExtension;
                log.LogInformation($"Filename: {filename}");

                cloudBlockBlob.Properties.ContentType = file.ContentType;
                cloudBlockBlob.Metadata.Add("origName", file.FileName);
                //var xff = req.Headers.FirstOrDefault(x => x.Key == "X-Forwarded-For").Value.FirstOrDefault();
                //cloudBlockBlob.Metadata.Add("sourceIp", xff);

                using (var stream = file.OpenReadStream()) {
                    await cloudBlockBlob.UploadFromStreamAsync(stream);
                }
                return (ActionResult)new OkObjectResult(new { name = filename });
            }
            return (ActionResult)new BadRequestResult();
        }

        private static string GetFirstValue(this IFormCollection form, string name) {
            string result = null;
            if (form.TryGetValue(name, out var sv) && sv.Count>0 && !string.IsNullOrEmpty(sv[0])) {
                result = sv[0];
            }
            return result;
        }
    }
}

