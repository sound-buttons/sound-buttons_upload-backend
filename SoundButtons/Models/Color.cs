using System.Text.Json.Serialization;

namespace SoundButtons.Models;
#nullable disable

public class Color
{
    [JsonPropertyName("primary")] public string Primary { get; set; }

    [JsonPropertyName("secondary")] public string Secondary { get; set; }
}