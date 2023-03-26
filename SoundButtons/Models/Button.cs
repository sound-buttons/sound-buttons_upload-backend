using Newtonsoft.Json;
using System;

namespace SoundButtons.Models;

public class Button
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonProperty("filename")]
    public string Filename { get; set; }

    [JsonProperty("text")]
    public object? Text { get; set; }

    [JsonProperty("baseRoute")]
    public string? BaseRoute { get; set; }

    private float _volume;
    [JsonProperty("volume")]
    public float Volume
    {
        get => _volume;
        set => _volume = value == 0
                             ? 1
                             : value;
    }

    [JsonProperty("source")]
    public Source Source { get; set; }

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
        this.Id = Guid.NewGuid().ToString();
        this.Filename = filename;
        this.Text = text;
        this.Volume = volume;
        this.Source = source;
    }
}

