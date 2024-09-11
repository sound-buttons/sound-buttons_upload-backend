using System.Text.Json.Serialization;

namespace SoundButtons.Models;
#nullable disable

public class Source
{
    public Source()
    {
    }

    public Source(string videoId, double start, double end)
    {
        VideoId = videoId;
        Start = start;
        End = end;
    }

    [JsonPropertyName("videoId")] public string VideoId { get; set; }

    [JsonPropertyName("start")] public double Start { get; set; }

    [JsonPropertyName("end")] public double End { get; set; }
}