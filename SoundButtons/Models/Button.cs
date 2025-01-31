using System;
using System.Text.Json.Serialization;

namespace SoundButtons.Models;

public class Button
{
    private float _volume;

    public Button()
    {
        Id = Guid.NewGuid().ToString();
        Filename = string.Empty;
#pragma warning disable CA2245 // 請勿將屬性指派給屬性自身
        Volume = Volume;
#pragma warning restore CA2245 // 請勿將屬性指派給屬性自身
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
        get => _volume;
        set => _volume = value == 0
                             ? 1
                             : value;
    }

    [JsonPropertyName("source")] public Source Source { get; set; }
}