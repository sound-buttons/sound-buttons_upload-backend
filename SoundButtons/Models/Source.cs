using Newtonsoft.Json;

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

    [JsonProperty("videoId")] public string VideoId { get; set; }

    [JsonProperty("start")] public double Start { get; set; }

    [JsonProperty("end")] public double End { get; set; }
}