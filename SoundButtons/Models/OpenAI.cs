#nullable disable

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SoundButtons.Models;

public class OpenAI
{
    public class TranscriptionsResponse
    {
        [JsonPropertyName("task")] public string Task { get; set; }

        [JsonPropertyName("language")] public string Language { get; set; }

        [JsonPropertyName("duration")] public double? Duration { get; set; }

        [JsonPropertyName("segments")] public List<Segment> Segments { get; set; }

        [JsonPropertyName("text")] public string Text { get; set; }
    }

    public class Segment
    {
        [JsonPropertyName("id")] public int? Id { get; set; }

        [JsonPropertyName("seek")] public int? Seek { get; set; }

        [JsonPropertyName("start")] public double? Start { get; set; }

        [JsonPropertyName("end")] public double? End { get; set; }

        [JsonPropertyName("text")] public string Text { get; set; }

        [JsonPropertyName("tokens")] public List<int?> Tokens { get; set; }

        [JsonPropertyName("temperature")] public double? Temperature { get; set; }

        [JsonPropertyName("avg_logprob")] public double? AvgLogprob { get; set; }

        [JsonPropertyName("compression_ratio")]
        public double? CompressionRatio { get; set; }

        [JsonPropertyName("no_speech_prob")] public double? NoSpeechProb { get; set; }

        [JsonPropertyName("transient")] public bool? Transient { get; set; }
    }
}