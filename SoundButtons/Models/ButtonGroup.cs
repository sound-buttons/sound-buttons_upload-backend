using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SoundButtons.Models;

public class ButtonGroup
{
    [JsonPropertyName("name")] public Text Name { get; set; } = new();

    [JsonPropertyName("baseRoute")] public string? BaseRoute { get; set; }

    [JsonPropertyName("buttons")] public List<Button> Buttons { get; set; } = [];
}