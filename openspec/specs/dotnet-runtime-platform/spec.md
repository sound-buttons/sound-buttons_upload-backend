# dotnet-runtime-platform Specification

## Purpose
Defines the .NET runtime and Azure Functions hosting platform for the `sound-buttons_upload-backend` service: the target framework (`net10.0`), the Functions v4 isolated-worker model and its minimum Worker package versions, the container build/runtime base images, which non-.NET tool versions must stay fixed, the externally observable behavior (HTTP, Durable, Blob, OpenAPI) that must be preserved across platform upgrades, and the CI/CD image-publish contract.
## Requirements
### Requirement: Target .NET runtime version

The `SoundButtons` Azure Functions project SHALL target .NET 10 (`net10.0`) and SHALL NOT retain any `net8.0` (or earlier) target. All framework-coupled references (for example the ASP.NET Core shared framework and `Microsoft.Extensions.*` packages) SHALL align with the .NET 10 release band.

#### Scenario: Project targets net10.0

- **WHEN** the `SoundButtons/SoundButtons.csproj` `<TargetFramework>` value is inspected
- **THEN** it equals `net10.0`
- **AND** no project file in the repository declares `net8.0`, `net7.0`, or `net6.0` as a primary build target

#### Scenario: Solution builds on the .NET 10 SDK

- **WHEN** `dotnet build` is run for the solution using the .NET 10 SDK
- **THEN** the build succeeds with no errors
- **AND** the build does not require any .NET 8 SDK to be installed

### Requirement: Azure Functions isolated worker compatibility

The project SHALL run on the Azure Functions v4 host using the .NET-isolated worker model, and the Azure Functions Worker core packages SHALL meet or exceed the minimum versions required by Microsoft to target .NET 10 (`Microsoft.Azure.Functions.Worker` ≥ 2.50.0 and `Microsoft.Azure.Functions.Worker.Sdk` ≥ 2.0.5). The `FUNCTIONS_WORKER_RUNTIME` SHALL remain `dotnet-isolated`.

#### Scenario: Worker core packages satisfy the .NET 10 minimum

- **WHEN** the package references in `SoundButtons.csproj` are inspected
- **THEN** `Microsoft.Azure.Functions.Worker` is at a version ≥ 2.50.0
- **AND** `Microsoft.Azure.Functions.Worker.Sdk` is at a version ≥ 2.0.5

#### Scenario: Isolated runtime identifier is preserved

- **WHEN** the runtime configuration (Dockerfile, helm deployment, and `local.settings.json`) is inspected
- **THEN** `FUNCTIONS_WORKER_RUNTIME` resolves to `dotnet-isolated`
- **AND** the Functions host major version remains `4`

### Requirement: Container build and runtime images on .NET 10

The container build stage SHALL use the .NET 10 SDK image, and the runtime stage SHALL use a .NET 10 Azure Functions isolated base image that exists in the official registry. Because no `-slim` variant is published for the .NET 10 isolated line, the runtime base image SHALL use a published .NET 10 isolated tag (for example `4-dotnet-isolated10.0`) rather than a non-existent `-slim` tag.

#### Scenario: Build image uses the .NET 10 SDK

- **WHEN** the `Dockerfile` build stage `FROM` instruction is inspected
- **THEN** it references `mcr.microsoft.com/dotnet/sdk:10.0`

#### Scenario: Runtime base image is a published .NET 10 isolated tag

- **WHEN** the `Dockerfile` base/runtime stage `FROM` instruction is inspected
- **THEN** it references a `mcr.microsoft.com/azure-functions/dotnet-isolated` tag for the `10.0` isolated line
- **AND** the referenced tag resolves to an existing manifest in the registry
- **AND** the tag does not use the unpublished `dotnet-isolated10.0-slim` form

#### Scenario: Image builds and the health endpoint responds

- **WHEN** the container image is built from the `Dockerfile` and started
- **THEN** the build completes successfully
- **AND** the container's `HEALTHCHECK` against `/api/healthz` reports healthy (confirming the app process and the copied `curl` binary run on the chosen .NET 10 base distro)

#### Scenario: Copied static tooling runs on the new base image

- **WHEN** an end-to-end upload is processed on the built image (per the OpenAPI/upload smoke test)
- **THEN** the copied static binaries (FFmpeg/ffprobe, yt-dlp, BgUtil POT, Deno, dumb-init) execute successfully on the .NET 10 base distro
- **AND** audio clipping completes without a missing-library or loader error

### Requirement: Non-.NET image and tool versions are unchanged

Components whose version tags are unrelated to the .NET runtime SHALL NOT be altered by this upgrade. In particular, the FFmpeg static-binary image tag (`ghcr.io/jim60105/static-ffmpeg-upx:8.1`, where `8.1` denotes the FFmpeg version) SHALL remain unchanged, as SHALL the yt-dlp, BgUtil POT, curl, and dumb-init sources. The Deno runtime binary (`denoland/deno:bin`) is an additional static tool binary added to the base stage; it SHALL NOT alter or replace any of the pre-existing tool sources.

#### Scenario: FFmpeg image tag is preserved

- **WHEN** the `Dockerfile` `COPY --from` instructions are inspected after the upgrade
- **THEN** the `ghcr.io/jim60105/static-ffmpeg-upx:8.1` reference is unchanged
- **AND** no FFmpeg-related tag was modified under the mistaken assumption that `8.1` referred to .NET 8

#### Scenario: Deno addition does not displace existing tool binaries

- **WHEN** the `Dockerfile` base stage is inspected after the change
- **THEN** the FFmpeg, ffprobe, bgutil-pot, bgutil-pot client, yt-dlp, and dumb-init instructions remain present and unmodified
- **AND** the Deno `COPY` is an addition, not a replacement

### Requirement: Externally observable behavior is preserved

The upgrade SHALL be a runtime/platform modernization only. The HTTP API surface (routes, authorization levels, request/response shapes), the Durable Functions orchestration and activity behavior, and the Blob Storage outputs SHALL remain functionally unchanged.

#### Scenario: HTTP contract unchanged

- **WHEN** the functions' triggers and routes are compared before and after the upgrade
- **THEN** the HTTP routes, methods, and authorization levels are identical
- **AND** no request or response schema is changed by the upgrade

#### Scenario: Durable orchestration continues to function

- **WHEN** an upload request is processed end-to-end after the upgrade
- **THEN** the orchestrator and its activity functions execute and complete as before
- **AND** the produced audio file and JSON configuration are written to Blob Storage as before

### Requirement: OpenAPI document remains available

The service SHALL continue to expose its OpenAPI/Swagger document at the same endpoint(s) it served before the upgrade. If no OpenAPI package can be made to function on .NET 10 / the Worker 2.x line, any reduction or removal of this capability SHALL be an explicit, documented decision rather than an accidental regression.

#### Scenario: OpenAPI endpoint returns a valid document

- **WHEN** the OpenAPI/Swagger endpoint is requested against the upgraded, running container
- **THEN** a valid OpenAPI document (and UI, where previously served) is returned
- **AND** it describes the same HTTP-triggered functions as before the upgrade

### Requirement: CI/CD publishes the upgraded image

The GitHub Actions image-publish workflow SHALL successfully build and push the container image using the .NET 10 base images, without requiring changes to its triggers or published image name.

#### Scenario: Publish workflow succeeds on the new platform

- **WHEN** the `docker_publish.yml` workflow runs against the upgraded `Dockerfile`
- **THEN** the multi-stage build completes on the .NET 10 SDK and isolated base images
- **AND** the image is pushed to `ghcr.io/sound-buttons/backend` as before

### Requirement: Source-generated, cached JSON serialization

The service SHALL perform JSON serialization and deserialization through cached `JsonSerializerOptions` instances backed by the System.Text.Json source generator (`SoundButtons.Json.SourceGenerationContext`), rather than allocating a new `JsonSerializerOptions` per invocation or relying solely on reflection-based metadata. The cached options SHALL combine the source-generated resolver with a reflection fallback resolver so that the polymorphic `Button.Text` (`object?`) member continues to round-trip. Adopting cached options SHALL eliminate the `CA1869` analyzer suppressions previously present in `ProcessJson`. Read strictness SHALL be preserved per call site: the config-JSON path keeps `AllowTrailingCommas = true`, while the OpenAI response deserialization keeps default strictness (it SHALL NOT begin accepting trailing commas or comments).

This modernization SHALL be behavior-preserving: the JSON wire format MUST be unchanged, specifically retaining the `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` encoder, indented output (`WriteIndented = true`) for written documents, and trailing-comma tolerance (`AllowTrailingCommas = true`) on the config-JSON read path.

#### Scenario: Serializer options are cached and source-generated

- **WHEN** the JSON serialization code in `ProcessJson` and `OpenAIService` is inspected
- **THEN** it uses cached `JsonSerializerOptions` instances whose `TypeInfoResolver` includes `SourceGenerationContext.Default`
- **AND** no `JsonSerializerOptions` is constructed per request inside the activity/serialization path
- **AND** no `#pragma warning disable CA1869` suppression remains in `ProcessJson`

#### Scenario: OpenAI response read strictness is preserved

- **WHEN** an OpenAI transcription response containing a trailing comma is deserialized after the change
- **THEN** deserialization fails exactly as it did before the change (the OpenAI options do not enable `AllowTrailingCommas`)

#### Scenario: JSON wire format is preserved byte-for-byte

- **WHEN** a representative `JsonRoot` configuration (including a `Button` whose `Text` is a JSON string and another whose `Text` is a JSON object) is serialized with the new cached options
- **THEN** the produced bytes are identical to those produced by the previous serialization configuration (UnsafeRelaxedJsonEscaping encoder, indented output)
- **AND** deserializing then re-serializing the same document yields identical bytes

#### Scenario: Source-generation context is used rather than dead code

- **WHEN** `SourceGenerationContext` is inspected
- **THEN** it declares `[JsonSerializable]` entries for the types it serializes (at least `JsonRoot` and `OpenAI.TranscriptionsResponse`)
- **AND** it is referenced by the serialization paths (it is not unused/dead code)

### Requirement: C# 14 field-backed properties for value normalization

Properties whose accessors exist solely to normalize a stored value SHALL use the C# 14 `field` keyword instead of a manually declared backing field, and SHALL NOT rely on constructor self-assignment workarounds or `CA2245` suppressions to trigger that normalization. The normalization behavior SHALL be preserved exactly.

#### Scenario: Button.Volume uses the field keyword without suppressions

- **WHEN** `SoundButtons/Models/Button.cs` is inspected
- **THEN** the `Volume` property setter uses the `field` keyword rather than an explicit `_volume` backing field
- **AND** no `Volume = Volume` self-assignment and no `#pragma warning disable CA2245` suppression remains

#### Scenario: Volume normalization behavior is unchanged

- **WHEN** a `Button` is created with a `volume` of `0` (or a `Button` is deserialized with `"volume": 0`)
- **THEN** its `Volume` resolves to `1`
- **AND** a `Button` created with a non-zero `volume` retains that exact value

