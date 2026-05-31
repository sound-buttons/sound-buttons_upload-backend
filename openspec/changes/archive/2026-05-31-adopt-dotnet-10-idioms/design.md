## Context

The `sound-buttons_upload-backend` service was migrated from .NET 8 to .NET 10 (C# 14) in the prior `upgrade-dotnet-10` change. That change was deliberately scoped as a runtime/platform move with no source-level modernization, so the codebase still carries pre-.NET-10 patterns and analyzer suppressions that the new SDK now lets us remove cleanly:

- `Button.Volume` (in `SoundButtons/Models/Button.cs`) declares an explicit `_volume` backing field purely to normalize `0 → 1` in its setter. The default constructor performs a `Volume = Volume` self-assignment (guarded by `#pragma warning disable CA2245`) just to trigger that normalization.
- `ProcessJson.ProcessJsonFile` allocates a fresh `JsonSerializerOptions` on every activity invocation in two places (each guarded by `#pragma warning disable CA1869`), and `OpenAIService.SpeechToTextAsync` calls `JsonSerializer.Deserialize<TranscriptionsResponse>(json)` with default options. All three paths use runtime-reflection serialization.
- `SoundButtons/Json/SourceGenerationContext.cs` already declares a `JsonSerializerContext` (`JsonRoot`, `Text`, `string`) but nothing references it — it is dead code.
- `ProcessAudioService` passes `new CancellationToken()` (twice) where `CancellationToken.None` is the idiomatic, self-documenting equivalent, and `ProcessAudioFromFileUpload` applies a dead `?? ""` to the non-nullable `Path.GetExtension(...)` result.

Constraints: the project is pre-release with 0 users and no backward-compatibility obligations, but the change must be **behavior-preserving** — in particular the JSON bytes written to Blob Storage must be byte-for-byte equivalent so existing config files keep their exact shape.

## Goals / Non-Goals

**Goals:**

- Adopt the C# 14 `field` keyword for `Button.Volume`, eliminating the manual backing field, the self-assignment workaround, and the `CA2245` suppression.
- Wire up and use the existing source-generated `JsonSerializerContext`, served through cached `static readonly JsonSerializerOptions` instances (one for the config-JSON read/write path and one for OpenAI deserialization), removing both `CA1869` suppressions and per-invocation options allocation.
- Preserve the exact JSON wire format (encoder, indentation, trailing-comma tolerance) and the polymorphic round-trip of `Button.Text`.
- Make minor, low-risk idiomatic cleanups (`CancellationToken.None`, dead `?? ""`).

**Non-Goals:**

- No HTTP/Durable/Blob/OpenAPI contract changes.
- No Native AOT or trimming enablement (Durable + OpenAPI extensions are not AOT-validated here); source generation is adopted for allocation/reflection reduction only, not as an AOT migration.
- No refactor of the `OpenAIService` static mutable `_apiKey` field, no migration to `IConfiguration`/`IOptions`, and no structured-logging conversion — these are unrelated to .NET 10 features and are explicitly deferred to keep the change tightly scoped.
- No change to `DateTime.Now` usages (deferred; would change timestamp semantics and is not a language-feature adoption).

## Decisions

### Decision 1: Use the C# 14 `field` keyword for `Button.Volume`

Replace the `_volume` field + setter with:

```csharp
[JsonPropertyName("volume")]
public float Volume
{
    get;
    set => field = value == 0 ? 1 : value;
}
```

The default constructor's `Volume = Volume` self-assignment (and its `CA2245` pragma) is removed; the auto-property initializer / setter normalization covers the default. Because the parameterless constructor currently sets `Volume = Volume` (reading the default `0` then writing `1`), the new code must ensure the same default of `1`. With a `field`-backed setter, an unset `Volume` would default to `0`, so the parameterless constructor will explicitly set `Volume = 1` (which is the value the old self-assignment produced) — preserving behavior without the suppression. **Alternative considered:** keep the explicit `_volume` field — rejected because removing it is the entire point and the `field` keyword is the idiomatic C# 14 replacement (the docs' canonical example matches this exact normalization pattern).

### Decision 2: Cached options (config + OpenAI) backed by source-gen + reflection fallback

Introduce cached `static readonly JsonSerializerOptions` (one for the config-JSON read/write path) configured as:

```csharp
new JsonSerializerOptions
{
    AllowTrailingCommas = true,
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    TypeInfoResolver = JsonTypeInfoResolver.Combine(
        SourceGenerationContext.Default,
        new DefaultJsonTypeInfoResolver()),
};
```

- `AllowTrailingCommas` only affects reads; `WriteIndented` only affects writes — combining both in one instance is equivalent to the two separate option sets used today in `ProcessJson`.
- The `Encoder` (`UnsafeRelaxedJsonEscaping`) cannot be expressed via `[JsonSourceGenerationOptions]`, so it is set on the runtime options object; the source generator still supplies the `JsonTypeInfo` metadata for the declared types.
- **The manually-constructed cached `JsonSerializerOptions` is authoritative**, not the `[JsonSourceGenerationOptions]` attribute on `SourceGenerationContext`. The existing attribute currently sets `ReadCommentHandling = JsonCommentHandling.Skip` and `AllowTrailingCommas = true`, but `ProcessJson`'s current read path does NOT skip comments — so the implementation MUST NOT use `SourceGenerationContext.Default.Options` directly. The now-misleading read settings on the attribute SHALL be removed/normalized so the only source of read/write behavior is the explicit cached options (preventing accidental comment tolerance). A test asserts comments remain rejected on the config-read path.
- `SourceGenerationContext` is extended with `[JsonSerializable(typeof(OpenAI.TranscriptionsResponse))]` so `OpenAIService` also uses source-generated metadata.
- **`OpenAIService` gets its own cached options instance.** It currently deserializes with default `JsonSerializerOptions` (no trailing-comma/comment tolerance). Reusing the relaxed config-JSON options would silently widen what malformed OpenAI responses are accepted. To preserve behavior, `OpenAIService` uses a separate `static readonly JsonSerializerOptions` with the same combined resolver but default read strictness (no `AllowTrailingCommas`).
- **`Button.Text` is `object?`** (intentionally polymorphic — a config "text" may be a JSON string or an object). The source generator cannot resolve `object`/`JsonElement`, so `JsonTypeInfoResolver.Combine(...)` adds a `DefaultJsonTypeInfoResolver()` fallback. This yields source-generated metadata for the declared model types and reflection only for the `object` member — exactly matching today's runtime-reflection behavior (on read `Text` becomes a `JsonElement`; on write the `JsonElement` is re-emitted). Note `IntroButton : Button` inherits `Text`, so the round-trip test must cover it too.

**Alternatives considered:** (a) Full source-gen with no reflection fallback — rejected: `object? Text` would throw `NotSupportedException` at runtime. (b) Caching plain reflection-based options without source-gen — rejected: leaves the existing `SourceGenerationContext` dead and forgoes the reflection-reduction benefit; the combined resolver achieves both with the same wire format.

### Decision 3: Bundle trivially-safe idiomatic cleanups

`new CancellationToken()` → `CancellationToken.None` (identical value, clearer intent) and remove the dead `?? ""` on `Path.GetExtension(...)` (which is declared non-nullable). These are co-located with the modernized files and carry no behavior change.

## Risks / Trade-offs

- **JSON output drift** (highest risk) → The change must not alter a single output byte. Mitigation: a round-trip test deserializes a representative config (including a `Button` with both string-valued and object-valued `Text`), re-serializes with the new cached options, and asserts the bytes are identical to serialization with the previous option configuration; the existing end-to-end upload path also verifies Blob output shape.
- **`field` keyword name collisions / accessor semantics** → `Button` has no member named `field`, and only the setter references it. Mitigation: covered by a `Volume` normalization unit test (`0 → 1`, non-zero preserved) and a JSON round-trip of `volume`.
- **Source-gen resolver ordering** → `JsonTypeInfoResolver.Combine` tries resolvers in order; `SourceGenerationContext.Default` must precede the reflection resolver so declared types use generated metadata. Mitigation: explicit ordering above, plus the round-trip test exercises both declared types and the `object` member.
- **`TranscriptionsResponse` source-gen** → It is a nested type with many nullable members; the generator handles these, but a deserialize test against a captured Whisper response payload guards equivalence.

## Migration Plan

This is a behavior-preserving internal refactor; no deploy-time migration or data backfill is required.

1. Apply the `field` keyword change to `Button.Volume`.
2. Add the cached options + extend `SourceGenerationContext`; route `ProcessJson` and `OpenAIService` through it; delete the inline options and `CA1869`/`CA2245` pragmas.
3. Apply the `CancellationToken.None` and dead-`?? ""` cleanups.
4. Add/extend unit tests (volume normalization, JSON round-trip byte-equivalence, `TranscriptionsResponse` deserialize) and confirm the existing coverage gate (≥85% line+branch) still passes via the Dockerfile `test` stage.

Rollback: revert the commit; there is no persisted state coupled to these changes.

## Open Questions

- None. The wire-format-preservation requirement makes the acceptance criteria unambiguous (byte-identical JSON output and unchanged volume normalization).
