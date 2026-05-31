using System;
using System.Text.Json.Serialization;

namespace SoundButtons.Models;

public class Button
{
    public Button()
    {
        Id = Guid.NewGuid().ToString();
        Filename = string.Empty;
        // Default to the normalized volume (a value of 0 is treated as 1 by the setter).
        Volume = 1;
        Source = new Source();
    }

    public Button(string filename, object text, float volume, Source source)
    {
        Id = Guid.NewGuid().ToString();
        Filename = filename;
        Text = text;
        Volume = volume;
        Source = source;
    }

    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("filename")] public string Filename { get; set; }

    [JsonPropertyName("text")] public object? Text { get; set; }

    [JsonPropertyName("baseRoute")] public string? BaseRoute { get; set; }

    [JsonPropertyName("volume")]
    public float Volume
    {
        get;
        set => field = value == 0
                           ? 1
                           : value;
    }

    [JsonPropertyName("source")] public Source Source { get; set; }
}
