using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using SoundButtons.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SoundButtons;

public partial class SoundButtons
{
    [FunctionName("UploadAudioToStorageAsync")]
    public async Task<Request> UploadAudioToStorageAsync(
           [ActivityTrigger] Request request,
           [Blob("sound-buttons"), StorageAccount("AzureStorage")] BlobContainerClient blobContainerClient)
    {
        using var _ = LogContext.PushProperty("InstanceId", request.instanceId);
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
        _logger.Information($"Filename: {filename + fileExtension}");

        // Write audio file 
        _logger.Information("Start to upload audio to blob storage {name}", blobContainerClient.Name);
        await cloudBlockBlob.UploadAsync(tempPath, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "audio/webm" } });
        _logger.Information("Upload audio to azure finish.");

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
}
