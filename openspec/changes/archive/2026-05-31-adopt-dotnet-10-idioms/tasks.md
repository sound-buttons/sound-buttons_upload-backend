## 1. C# 14 field-backed property

- [x] 1.1 In `SoundButtons/Models/Button.cs`, rewrite `Volume` to use the C# 14 `field` keyword (`get; set => field = value == 0 ? 1 : value;`) and remove the explicit `_volume` backing field.
- [x] 1.2 Remove the parameterless constructor's `Volume = Volume` self-assignment and its `#pragma warning disable/restore CA2245`; ensure the default `Volume` still resolves to `1` (set `Volume = 1` explicitly in the parameterless constructor).
- [x] 1.3 Confirm the parameterized constructor still assigns `Volume = volume` and that the `[JsonPropertyName("volume")]` attribute is retained.

## 2. Source-generated, cached JSON serialization

- [x] 2.1 In `SoundButtons/Json/SourceGenerationContext.cs`, add `[JsonSerializable(typeof(OpenAI.TranscriptionsResponse))]` (and keep `JsonRoot`); normalize/remove the existing `[JsonSourceGenerationOptions(...)]` read settings (`ReadCommentHandling`, `AllowTrailingCommas`) so they cannot leak into runtime behavior. Define a cached config-JSON `static readonly JsonSerializerOptions` that sets `AllowTrailingCommas = true`, `WriteIndented = true`, `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, and `TypeInfoResolver = JsonTypeInfoResolver.Combine(SourceGenerationContext.Default, new DefaultJsonTypeInfoResolver())`. Document that this explicit options object — not `SourceGenerationContext.Default.Options` — is authoritative.
- [x] 2.2 In `SoundButtons/Functions/ProcessJson.cs`, replace both inline `new JsonSerializerOptions { ... }` blocks (read and write) with the shared cached config-JSON options, and delete both `#pragma warning disable/restore CA1869` suppressions.
- [x] 2.3 In `SoundButtons/Services/OpenAIService.cs`, deserialize via a SEPARATE cached `static readonly JsonSerializerOptions` that uses the same combined source-gen + reflection resolver but KEEPS default read strictness (no `AllowTrailingCommas`, no comment skipping), preserving the current behavior for OpenAI responses.
- [x] 2.4 Verify the `Encoder` is applied at runtime (not via `[JsonSourceGenerationOptions]`), that the reflection fallback resolver covers the polymorphic `Button.Text` (`object?`) member (including via `IntroButton : Button`), and that the resolver ordering places `SourceGenerationContext.Default` before `DefaultJsonTypeInfoResolver`.

## 3. Idiomatic cleanups

- [x] 3.1 In `SoundButtons/Services/ProcessAudioService.cs`, replace both `new CancellationToken()` occurrences with `CancellationToken.None`.
- [x] 3.2 In `SoundButtons/Functions/SoundButtons.cs`, remove the dead `?? ""` on the non-nullable `Path.GetExtension(file.Key)` result.

## 4. Tests

- [x] 4.1 Add/extend a unit test asserting `Button.Volume` normalization: `0 → 1` (constructor, deserialized `"volume": 0`, AND deserialized JSON with `volume` omitted entirely) and non-zero values preserved.
- [x] 4.2 Add a JSON round-trip test using a realistic full `JsonRoot` fixture that exercises: a `Button` with string-valued `Text` and a `Button` with object-valued `Text`, an `IntroButton`, Unicode plus `&`/`<`/`>` content, a null optional property, and a button with missing/zero `volume`. Serialize with the new cached config-JSON options and assert byte-identical UTF-8 output versus the previous (pre-change) read+write option configuration; also assert deserialize→serialize is byte-stable.
- [x] 4.3 Add a deserialize test for `OpenAI.TranscriptionsResponse` against a captured Whisper JSON payload, asserting field equivalence with the previous reflection-based path, AND a test confirming the OpenAI options still reject trailing commas/comments (read strictness preserved).
- [x] 4.4 Add a test confirming the config-read path still rejects JSON comments (i.e., the source-gen attribute's old `ReadCommentHandling = Skip` did not leak into runtime behavior).

## 5. Verification

- [x] 5.1 Build and run the test suite in the .NET 10 podman container (or Dockerfile `test` stage); confirm all tests pass and the ≥85% line+branch coverage gate still passes.
- [x] 5.2 Confirm no `CA1869` or `CA2245` suppressions remain in the touched files and the build produces no new analyzer warnings.
- [x] 5.3 Mark every task in this file `[x]` once complete.
