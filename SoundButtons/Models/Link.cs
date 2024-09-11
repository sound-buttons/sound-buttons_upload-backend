using System.Text.Json.Serialization;

namespace SoundButtons.Models;
#nullable disable

public class Link
{
    [JsonPropertyName("youtube")] public string Youtube { get; set; }

    [JsonPropertyName("twitch")] public string Twitch { get; set; }

    [JsonPropertyName("twitter")] public string Twitter { get; set; }

    [JsonPropertyName("facebook")] public string Facebook { get; set; }

    [JsonPropertyName("instagram")] public string Instagram { get; set; }

    [JsonPropertyName("discord")] public string Discord { get; set; }

    [JsonPropertyName("other")] public string Other { get; set; }
}