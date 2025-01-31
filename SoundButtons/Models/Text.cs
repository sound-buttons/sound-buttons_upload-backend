using System.Text.Json.Serialization;

namespace SoundButtons.Models;

public class Text
{
    public Text()
    {
    }

    public Text(string zhTw, string ja)
    {
        ZhTw = zhTw;
        Ja = ja;
    }

    [JsonPropertyName("zh-tw")] public string? ZhTw { get; set; }

    [JsonPropertyName("ja")] public string? Ja { get; set; }
}