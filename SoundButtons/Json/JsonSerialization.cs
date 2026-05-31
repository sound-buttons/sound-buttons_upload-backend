using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace SoundButtons.Json;

// Cached JsonSerializerOptions for the service. Defined outside SourceGenerationContext.cs
// (which is excluded from coverage) so this hand-written configuration is measured.
internal static class JsonSerialization
{
    // Combine source-generated metadata (fast, allocation-light) with a reflection fallback
    // so the polymorphic Button.Text (object?) member keeps round-tripping exactly as before.
    private static IJsonTypeInfoResolver Resolver { get; }
        = JsonTypeInfoResolver.Combine(SourceGenerationContext.Default, new DefaultJsonTypeInfoResolver());

    // Config-JSON read/write options. Mirrors the historical ProcessJson behavior:
    // relaxed Unicode/'&' escaping, indented output, and trailing-comma tolerance on read.
    internal static readonly JsonSerializerOptions ConfigJson = new()
    {
        AllowTrailingCommas = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = Resolver
    };

    // OpenAI transcription deserialization. Keeps default read strictness (no trailing
    // commas / comments) to preserve the previous default-options behavior.
    internal static readonly JsonSerializerOptions OpenAi = new()
    {
        TypeInfoResolver = Resolver
    };
}
