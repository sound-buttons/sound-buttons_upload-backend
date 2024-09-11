using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using SoundButtons.Models;

namespace SoundButtons.Functions;

public class ProcessJson
{
    private readonly BlobContainerClient _blobContainerClient;
    private readonly ILogger<ProcessJson> _logger;

    public ProcessJson(ILogger<ProcessJson> logger, IAzureClientFactory<BlobServiceClient> blobClientFactory)
    {
        _logger = logger;
        _blobContainerClient = blobClientFactory.CreateClient("sound-buttons").GetBlobContainerClient("sound-buttons");
        _blobContainerClient.CreateIfNotExists();
    }

    [Function("ProcessJsonFile")]
    public async Task ProcessJsonFile(
        [ActivityTrigger] Request request)
    {
        using IDisposable _ = LogContext.PushProperty("InstanceId", request.InstanceId);
        Source source = request.Source;
        string directory = request.Directory;
        string filename = request.Filename;
        string fileExtension = Path.GetExtension(request.TempPath);
        // Get last json file
        BlobClient jsonBlob = _blobContainerClient.GetBlobClient($"{directory}/{directory}.json");
        if (!(await jsonBlob.ExistsAsync()).Value)
        {
            _logger.LogCritical("{jsonFile} not found!!", jsonBlob.Name);
            return;
        }

        _logger.LogInformation("Read Json file {name}", jsonBlob.Name);

        JsonRoot? root;
        // Read last json file
        using (MemoryStream ms = new())
        {
            try
            {
                await (await jsonBlob.OpenReadAsync()).CopyToAsync(ms);
            }
            catch (OutOfMemoryException)
            {
                _logger.LogError("System.OutOfMemoryException!! Directly try again.");
                // Retry and let it fail if it comes up again.
                await (await jsonBlob.OpenReadAsync(new BlobOpenReadOptions(false)
                          {
                              BufferSize = 8192,
                              Position = 0
                          })).CopyToAsync(ms);
            }

            ms.Seek(0, SeekOrigin.Begin);
#pragma warning disable CA1869
            var serializerOptions = new JsonSerializerOptions
#pragma warning restore CA1869
            {
                // Allow trailing commas in JSON
                AllowTrailingCommas = true,
                // For Unicode and '&' characters
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            using var streamReader = new StreamReader(ms);
            var jsonString = await streamReader.ReadToEndAsync();
            root = JsonSerializer.Deserialize<JsonRoot>(jsonString, serializerOptions);
        }

        if (null == root)
        {
            _logger.LogCritical("{jsonFile} is json invalid!!", jsonBlob.Name);
            return;
        }

        // Get new json file block
        BlobClient newJsonBlob = _blobContainerClient.GetBlobClient($"{directory}/UploadJson/{DateTime.Now:yyyy-MM-dd-HH-mm}.json");

        // Generate new json file
        JsonRoot json = UpdateJson(root,
                                   directory,
                                   filename + fileExtension,
                                   request,
                                   source
        );

#pragma warning disable CA1869 // 快取並重新使用 'JsonSerializerOptions' 執行個體
        byte[] result = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));
#pragma warning restore CA1869 // 快取並重新使用 'JsonSerializerOptions' 執行個體

        _logger.LogInformation("Write Json {name}", jsonBlob.Name);
        _logger.LogInformation("Write Json backup {name}", newJsonBlob.Name);

        // Write new json file
        var option = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" } };
        await Task.WhenAll(newJsonBlob.UploadAsync(new BinaryData(result), option),
                           jsonBlob.UploadAsync(new BinaryData(result), option));
    }

    private JsonRoot UpdateJson(JsonRoot root, string directory, string filename, Request request, Source source)
    {
        _logger.LogInformation("Update Json");

        // Variables prepare
        var baseRoute = $"https://soundbuttons.blob.core.windows.net/sound-buttons/{directory}/";

        string group = request.Group;

        // Get ButtonGroup if exists, or new one
        ButtonGroup? buttonGroup = root.ButtonGroups.Find(p => p.Name?.ZhTw == group || p.Name?.Ja == group);
        if (buttonGroup == null)
        {
            buttonGroup = new ButtonGroup
            {
                Name = new Text(group, group),
                BaseRoute = baseRoute,
                Buttons = []
            };

            root.ButtonGroups.Add(buttonGroup);
        }
        else if (string.IsNullOrEmpty(buttonGroup.Name.Ja))
        {
            buttonGroup.Name.Ja = buttonGroup.Name.ZhTw;
        }

        // Prevent script injection
        source.VideoId = HttpUtility.UrlEncode(source.VideoId);

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