using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using SoundButtons.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SoundButtons;

public partial class SoundButtons
{
    [FunctionName("UploadAudioToStorageAsync")]
    public static async Task<Request> UploadAudioToStorageAsync(
           [ActivityTrigger] Request request,
           ILogger log,
           [Blob("sound-buttons"), StorageAccount("AzureStorage")] BlobContainerClient blobContainerClient)
    {
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
        log.LogInformation($"Filename: {filename + fileExtension}");

        try
        {
            // Write audio file 
            log.LogInformation("Start to upload audio to blob storage {name}", blobContainerClient.Name);
            await cloudBlockBlob.UploadAsync(tempPath, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "audio/webm" } });
            log.LogInformation("Upload audio to azure finish.");
        }
        finally { File.Delete(tempPath); }

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
