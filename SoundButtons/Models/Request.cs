using System.Text.Json.Serialization;

namespace SoundButtons.Models;
#nullable disable

public class Request
{
    [JsonPropertyName("ip")] public string Ip { get; set; }

    [JsonPropertyName("filename")] public string Filename { get; set; }

    [JsonPropertyName("directory")] public string Directory { get; set; }

    [JsonPropertyName("source")] public Source Source { get; set; }

    [JsonPropertyName("clip")] public string Clip { get; set; }

    [JsonPropertyName("nameZH")] public string NameZH { get; set; }

    [JsonPropertyName("nameJP")] public string NameJP { get; set; }

    [JsonPropertyName("volume")] public float Volume { get; set; }

    [JsonPropertyName("group")] public string Group { get; set; }

    [JsonPropertyName("tempPath")] public string TempPath { get; set; }

    [JsonPropertyName("toastId")] public string ToastId { get; set; }

    [JsonPropertyName("instanceId")] public string InstanceId { get; internal set; }
}