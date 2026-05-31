using System.Text.Json;
using SoundButtons.Models;
using Xunit;

namespace SoundButtons.Tests.Models;

[Trait("spec", "audio-submission-api")]
public class ButtonTests
{
    [Fact]
    public void DefaultConstructor_SetsDefaults()
    {
        var button = new Button();

        Assert.False(string.IsNullOrEmpty(button.Id));
        Assert.Equal(string.Empty, button.Filename);
        Assert.NotNull(button.Source);
        // Volume getter defaults to 1 because the setter normalizes 0 to 1.
        Assert.Equal(1f, button.Volume);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 2f)]
    public void Volume_NormalizesZeroToOne(float input, float expected)
    {
        var button = new Button { Volume = input };

        Assert.Equal(expected, button.Volume);
    }

    [Fact]
    public void Deserialize_ExplicitZeroVolume_NormalizesToOne()
    {
        Button? button = JsonSerializer.Deserialize<Button>("{\"filename\":\"f.webm\",\"volume\":0}");

        Assert.NotNull(button);
        Assert.Equal(1f, button!.Volume);
    }

    [Fact]
    public void Deserialize_OmittedVolume_DefaultsToOne()
    {
        // The parameterless constructor runs first (setting Volume to 1); with no "volume"
        // member in the payload the setter is never invoked, so the default of 1 is retained.
        Button? button = JsonSerializer.Deserialize<Button>("{\"filename\":\"f.webm\"}");

        Assert.NotNull(button);
        Assert.Equal(1f, button!.Volume);
    }

    [Fact]
    public void Deserialize_NonZeroVolume_IsPreserved()
    {
        Button? button = JsonSerializer.Deserialize<Button>("{\"filename\":\"f.webm\",\"volume\":0.3}");

        Assert.NotNull(button);
        Assert.Equal(0.3f, button!.Volume);
    }

    [Fact]
    public void ParameterizedConstructor_AssignsProperties()
    {
        var source = new Source("abc", 1, 2);
        var text = new Text("中文", "日本語");

        var button = new Button("file.webm", text, 0.3f, source);

        Assert.Equal("file.webm", button.Filename);
        Assert.Same(text, button.Text);
        Assert.Equal(0.3f, button.Volume);
        Assert.Same(source, button.Source);
        Assert.False(string.IsNullOrEmpty(button.Id));
    }
}
