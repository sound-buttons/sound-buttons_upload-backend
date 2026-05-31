using System.Text.Encodings.Web;
using System.Text.Json;
using SoundButtons.Json;
using SoundButtons.Models;
using Xunit;
using static SoundButtons.Models.OpenAI;

namespace SoundButtons.Tests.Json;

[Trait("spec", "blob-storage-publishing")]
public class JsonSerializationTests
{
    // Reconstructs the exact option configuration ProcessJson used before this change:
    // reflection-based metadata, relaxed escaping, indented writes, trailing-comma reads.
    private static readonly JsonSerializerOptions LegacyWrite = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions LegacyRead = new()
    {
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static JsonRoot BuildRepresentativeRoot()
        => new()
        {
            Name = "char",
            FullName = "Character & <Friends> 日本語",
            FullConfigURL = "https://example.com/config.json",
            ImgSrc = ["https://example.com/a.png", "https://example.com/b.png"],
            Intro = "Intro with & < > and 日本語 emoji 🎵",
            Color = new Color { Primary = "#fff", Secondary = "#000" },
            Link = new Link { Youtube = "https://youtu.be/x", Twitter = null },
            // IntroButton inherits Button (and its polymorphic Text + normalized Volume).
            IntroButton = new IntroButton
            {
                Filename = "intro.webm",
                Text = new Text("自己紹介", "イントロ"),
                Source = new Source("vid0", 0, 1)
            },
            ButtonGroups =
            [
                new ButtonGroup
                {
                    Name = new Text("グループ", "group"),
                    BaseRoute = "https://example.com/g/",
                    Buttons =
                    [
                        // Text as a plain JSON string.
                        new Button
                        {
                            Filename = "s.webm",
                            Text = "string text & <b> 日本語",
                            BaseRoute = null,
                            Volume = 0.5f,
                            Source = new Source("vid1", 1, 2)
                        },
                        // Text as an object.
                        new Button
                        {
                            Filename = "o.webm",
                            Text = new Text("物件文字", "object text"),
                            Volume = 2f,
                            Source = new Source("vid2", 3, 4)
                        }
                    ]
                }
            ]
        };

    [Fact]
    public void ConfigJson_Serialize_ProducesByteIdenticalOutputVersusLegacyOptions()
    {
        JsonRoot root = BuildRepresentativeRoot();

        string viaNew = JsonSerializer.Serialize(root, JsonSerialization.ConfigJson);
        string viaLegacy = JsonSerializer.Serialize(root, LegacyWrite);

        Assert.Equal(viaLegacy, viaNew);
    }

    [Fact]
    public void ConfigJson_RoundTrip_IsByteStable()
    {
        JsonRoot root = BuildRepresentativeRoot();

        string first = JsonSerializer.Serialize(root, JsonSerialization.ConfigJson);
        JsonRoot? parsed = JsonSerializer.Deserialize<JsonRoot>(first, JsonSerialization.ConfigJson);
        string second = JsonSerializer.Serialize(parsed, JsonSerialization.ConfigJson);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ConfigJson_Deserialize_MatchesLegacyReadPath()
    {
        // Serialize once, then re-serialize what each read path produced; the bytes must match.
        string doc = JsonSerializer.Serialize(BuildRepresentativeRoot(), LegacyWrite);

        string viaNew = JsonSerializer.Serialize(
            JsonSerializer.Deserialize<JsonRoot>(doc, JsonSerialization.ConfigJson), LegacyWrite);
        string viaLegacy = JsonSerializer.Serialize(
            JsonSerializer.Deserialize<JsonRoot>(doc, LegacyRead), LegacyWrite);

        Assert.Equal(viaLegacy, viaNew);
    }

    [Fact]
    public void ConfigJson_Read_RejectsComments()
    {
        // ProcessJson never enabled comment skipping; ensure the source-gen attribute's old
        // ReadCommentHandling = Skip did not leak into the runtime options.
        const string withComment = "{\n  // a comment\n  \"name\": \"x\"\n}";

        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<JsonRoot>(withComment, JsonSerialization.ConfigJson));
    }

    [Fact]
    public void OpenAi_Deserialize_MatchesLegacyDefaultOptions()
    {
        const string payload =
            "{\"task\":\"transcribe\",\"language\":\"ja\",\"duration\":1.5," +
            "\"text\":\"こんにちは & <x>\",\"segments\":[{\"id\":0,\"text\":\"seg\"}]}";

        TranscriptionsResponse? viaNew = JsonSerializer.Deserialize<TranscriptionsResponse>(payload, JsonSerialization.OpenAi);
        TranscriptionsResponse? viaLegacy = JsonSerializer.Deserialize<TranscriptionsResponse>(payload);

        Assert.NotNull(viaNew);
        Assert.NotNull(viaLegacy);
        Assert.Equal(viaLegacy!.Task, viaNew!.Task);
        Assert.Equal(viaLegacy.Language, viaNew.Language);
        Assert.Equal(viaLegacy.Duration, viaNew.Duration);
        Assert.Equal(viaLegacy.Text, viaNew.Text);
        Assert.Equal(viaLegacy.Segments![0].Text, viaNew.Segments![0].Text);
    }

    [Fact]
    public void OpenAi_Read_RejectsTrailingCommas()
    {
        // The OpenAI path must keep default strictness (the previous code used default options).
        const string trailingComma = "{\"text\":\"a\",}";

        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<TranscriptionsResponse>(trailingComma, JsonSerialization.OpenAi));
    }
}
