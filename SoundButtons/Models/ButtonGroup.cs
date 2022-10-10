using System.Collections.Generic;

namespace SoundButtons.Models;

public class ButtonGroup
{
    public Text name { get; set; }
    public string baseRoute { get; set; }
    public List<Button> buttons { get; set; }

    public ButtonGroup() { }
}
