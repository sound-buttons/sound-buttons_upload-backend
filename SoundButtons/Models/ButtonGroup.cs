using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SoundButtons.Models;

public class ButtonGroup
{
    public ButtonGroup()
    {
        Buttons = new List<Button>();
        Name = new Text();
    }

    [JsonPropertyName("name")] public Text Name { get; set; }

    [JsonPropertyName("baseRoute")] public string? BaseRoute { get; set; }

    [JsonPropertyName("buttons")] public List<Button> Buttons { get; set; }
}