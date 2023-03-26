using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Serilog;
using Serilog.Context;
using SoundButtons.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace SoundButtons.Functions;

public class ProcessJson
{
    private static ILogger Logger => Helper.Log.Logger;

    [FunctionName("ProcessJsonFile")]
    public static async Task ProcessJsonFile(
    [ActivityTrigger] Request request,
    [Blob("sound-buttons"), StorageAccount("AzureStorage")] BlobContainerClient BlobContainerClient)
    {
        using var _ = LogContext.PushProperty("InstanceId", request.InstanceId);
        Source source = request.Source;
        string directory = request.Directory;
        string filename = request.Filename;
        string fileExtension = Path.GetExtension(request.TempPath);
        // Get last json file
        BlobClient jsonBlob = BlobContainerClient.GetBlobClient($"{directory}/{directory}.json");
        if (!jsonBlob.Exists().Value)
        {
            Logger.Fatal("{jsonFile} not found!!", jsonBlob.Name);
            return;
        }
        Logger.Information("Read Json file {name}", jsonBlob.Name);

        JsonRoot? root;
        // Read last json file
        using (MemoryStream ms = new())
        {
            try
            {
                await jsonBlob.OpenRead().CopyToAsync(ms);
            }
            catch (OutOfMemoryException)
            {
                Logger.Error("System.OutOfMemoryException!! Directly try again.");
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

        if (null == root)
        {
            Logger.Fatal("{jsonFile} is json invalid!!", jsonBlob.Name);
            return; 
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

        Logger.Information("Write Json {name}", jsonBlob.Name);
        Logger.Information("Write Json backup {name}", newjsonBlob.Name);

        // Write new json file
        var option = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" } };
        await Task.WhenAll(newjsonBlob.UploadAsync(new BinaryData(result), option),
                           jsonBlob.UploadAsync(new BinaryData(result), option));
    }

    private static JsonRoot UpdateJson(JsonRoot root, string directory, string filename, Request request, Source source)
    {
        Logger.Information("Update Json");

        // Variables prepare
        string baseRoute = $"https://soundbuttons.blob.core.windows.net/sound-buttons/{directory}/";

        string group = request.Group;

        // Get ButtonGrop if exists, or new one
        ButtonGroup? buttonGroup = root.ButtonGroups.Find(p=>p.Name?.ZhTw == group || p.Name?.Ja == group);
        if (buttonGroup == null)
        {
            buttonGroup = new ButtonGroup
            {
                Name = new Text(group, group),
                BaseRoute = baseRoute,
                Buttons = new List<Button>()
            };
            root.ButtonGroups.Add(buttonGroup);
        }
        else if (null != buttonGroup.Name && string.IsNullOrEmpty(buttonGroup.Name.Ja))
        {
            buttonGroup.Name.Ja = buttonGroup.Name.ZhTw;
        }

        // Prevent script injection
        source.VideoId = System.Web.HttpUtility.UrlEncode(source.VideoId);

        // Add button

        buttonGroup.Buttons.Add(new Button(
            filename,
            new Text(
                request.NameZH,
                request.NameJP
            ),
            request.Volume,
            source
        ));

        return root;
    }
}
