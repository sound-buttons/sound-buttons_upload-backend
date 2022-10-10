using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using SoundButtons.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace SoundButtons;

public partial class SoundButtons
{
    [FunctionName("ProcessJsonFile")]
    public static async Task ProcessJsonFile(
    [ActivityTrigger] Request request,
    ILogger log,
    [Blob("sound-buttons"), StorageAccount("AzureStorage")] BlobContainerClient BlobContainerClient)
    {
        Source source = request.source;
        string directory = request.directory;
        string filename = request.filename;
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

            ms.Seek(0, SeekOrigin.Begin);
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
                                   source
                                   );
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

    private static JsonRoot UpdateJson(JsonRoot root, string directory, string filename, Request request, Source source)
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
            source
        ));

        return root;
    }
}
