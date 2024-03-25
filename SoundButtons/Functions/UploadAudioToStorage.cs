using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Serilog;
using Serilog.Context;
using SoundButtons.Models;
using Log = SoundButtons.Helper.Log;

namespace SoundButtons.Functions;

public class UploadAudioToStorage
{
    private static ILogger Logger => Log.Logger;

    [FunctionName("UploadAudioToStorageAsync")]
    public async Task<Request> UploadAudioToStorageAsync(
        [ActivityTrigger] Request request,
        [Blob("sound-buttons")] [StorageAccount("AzureStorage")]
        BlobContainerClient blobContainerClient)
    {
        using IDisposable _ = LogContext.PushProperty("InstanceId", request.InstanceId);
        string ip = request.Ip;
        string filename = request.Filename;
        string directory = request.Directory;
        string tempPath = request.TempPath;
        string fileExtension = Path.GetExtension(tempPath);

        // Get a new file name on blob storage
        BlobClient cloudBlockBlob = blobContainerClient.GetBlobClient($"{directory}/{filename + fileExtension}");
        if (await cloudBlockBlob.ExistsAsync())
        {
            filename += $"_{DateTime.Now.Ticks}";
            cloudBlockBlob = blobContainerClient.GetBlobClient($"{directory}/{filename + fileExtension}");
        }

        request.Filename = filename;
        Logger.Information($"Filename: {filename + fileExtension}");

        // Write audio file 
        Logger.Information("Start to upload audio to blob storage {name}", blobContainerClient.Name);
        await cloudBlockBlob.UploadAsync(tempPath, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "audio/webm" } });
        Logger.Information("Upload audio to azure finish.");

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