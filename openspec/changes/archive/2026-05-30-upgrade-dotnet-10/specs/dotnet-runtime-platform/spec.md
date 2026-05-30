## ADDED Requirements

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
- **THEN** the copied static binaries (FFmpeg/ffprobe, yt-dlp, BgUtil POT, dumb-init) execute successfully on the .NET 10 base distro
- **AND** audio clipping completes without a missing-library or loader error

### Requirement: Non-.NET image and tool versions are unchanged

Components whose version tags are unrelated to the .NET runtime SHALL NOT be altered by this upgrade. In particular, the FFmpeg static-binary image tag (`ghcr.io/jim60105/static-ffmpeg-upx:8.0`, where `8.0` denotes the FFmpeg version) SHALL remain unchanged, as SHALL the yt-dlp, BgUtil POT, curl, and dumb-init sources.

#### Scenario: FFmpeg image tag is preserved

- **WHEN** the `Dockerfile` `COPY --from` instructions are inspected after the upgrade
- **THEN** the `ghcr.io/jim60105/static-ffmpeg-upx:8.0` reference is unchanged
- **AND** no FFmpeg-related tag was modified under the mistaken assumption that `8.0` referred to .NET 8

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
