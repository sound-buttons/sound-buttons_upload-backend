## 1. Preparation

- [ ] 1.1 Create a feature branch off `master` (e.g. `upgrade/dotnet-10`). **PENDING:** changes currently applied on `master` working tree (uncommitted); branch/commit awaits the user's go-ahead.
- [x] 1.2 Confirm the local toolchain has the .NET 10 SDK available (`dotnet --list-sdks` shows a `10.0.x` SDK); confirm Docker/Podman can pull `mcr.microsoft.com/dotnet/sdk:10.0` and `mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0`.
- [x] 1.3 Re-verify the latest stable NuGet versions for every package in the design's Decision 1 table (the design lists the values found at proposal time; confirm none have moved before pinning). Also confirm the latest stable `Serilog` core version.

## 2. Project file (SoundButtons.csproj)

- [x] 2.1 Change `<TargetFramework>` from `net8.0` to `net10.0`.
- [x] 2.2 **(Required)** Upgrade the mandatory worker core packages: `Microsoft.Azure.Functions.Worker` → `2.52.0` and `Microsoft.Azure.Functions.Worker.Sdk` → `2.0.7` (must be ≥ 2.50.0 / ≥ 2.0.5 respectively for net10.0).
- [x] 2.3 **(Required)** Upgrade the framework-coupled package needed by the 2.x worker: `...Extensions.Http.AspNetCore` → `2.1.0` (depends on Worker ≥ 2.1.0). For `System.Text.Json`: either bump to the latest stable 10.0 patch (pin the exact `10.0.N` after verification) or remove the explicit reference (in-box on net10.0).
- [x] 2.4 **(Required)** Build and validate with only the required set above (proceed through task groups 3–6) BEFORE applying the optional currency bumps below, so any regression can be bisected to the right group.
- [x] 2.5 **(Optional currency bumps — apply as a separate, independently-revertable step after 2.4 validates)** `...Extensions.Storage.Blobs` → `6.8.1`, `...Extensions.DurableTask` → `1.16.5`, `...Worker.ApplicationInsights` → `2.50.0`, `Microsoft.ApplicationInsights.WorkerService` → `3.1.2`, `Microsoft.Extensions.Configuration.UserSecrets` → latest stable `10.0.N`, `Azure.Storage.Blobs` → `12.28.0`, `Serilog` → `4.3.1`, `Serilog.AspNetCore` → `10.0.0`, `Serilog.Sinks.Console` → `6.1.1`, `Serilog.Sinks.Seq` → `9.1.0`, `Xabe.FFmpeg` → `6.0.2`, `YoutubeDLSharp` → `1.2.0`. Re-run the validation (groups 3 & 6) after applying. Any single bump that misbehaves may be deferred to its own follow-up change without blocking the runtime upgrade.
- [x] 2.6 ~~Set `Microsoft.Azure.WebJobs.Extensions.OpenApi` → `1.6.0`~~ **Replaced** with the isolated-worker `Microsoft.Azure.Functions.Worker.Extensions.OpenApi` `1.6.0` — the spike (task 6.3) found the in-proc package registers no endpoints on the isolated worker (all routes 404). Fallback (a) per design Decision 4 applied; validated working.
- [x] 2.7 Evaluate removing the legacy explicit `System.Net.Http` (`4.3.4`) and `System.Text.RegularExpressions` (`4.3.1`) pins; remove if the clean build (task 3.2) succeeds without them.
- [x] 2.8 Keep `<AzureFunctionsVersion>v4</AzureFunctionsVersion>` and the `FrameworkReference Microsoft.AspNetCore.App` unchanged. Pin every package to an exact patch version (no floating `*`) after verifying against NuGet.

## 3. Build and compile fixes

- [x] 3.1 Delete generated output: `rm -rf SoundButtons/obj SoundButtons/bin`.
- [x] 3.2 Run `dotnet restore` then `dotnet build -c Release` on the .NET 10 SDK; resolve any compile errors introduced by the Worker 2.x API surface.
- [x] 3.3 Audit the Worker 1.x→2.x behavioral breaks against the code (per design Decision 5): confirm no `HttpResponseData.WriteAsJsonAsync` status-code dependency, no `ILoggerExtensions`/`LogMetric` usage, no batch/collection triggers, and that DI scope validation passes (no singleton capturing a scoped service). Fix anything that surfaces.
- [x] 3.4 Confirm the auto-generated `WorkerExtensions`/functions metadata project builds cleanly after regeneration (do not hand-edit generated files).

## 4. Source code

- [x] 4.1 In `SoundButtons/Program.cs`, update the outbound HTTP `User-Agent` product version from `.NET`/`8.0` to `.NET`/`10.0`.
- [x] 4.2 Verify the `HostBuilder().ConfigureFunctionsWebApplication()` startup still compiles and runs (no migration to `FunctionsApplication.CreateBuilder` required).

## 5. Dockerfile

- [x] 5.1 Change the build stage base from `mcr.microsoft.com/dotnet/sdk:8.0` to `mcr.microsoft.com/dotnet/sdk:10.0`.
- [x] 5.2 Change the runtime base stage from `mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0-slim` to `mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0` (NOTE: there is **no** `-slim` tag for the .NET 10 isolated line; do not use `...10.0-slim`).
- [x] 5.3 Leave all non-.NET sources unchanged: `ghcr.io/jim60105/static-ffmpeg-upx:8.0` (FFmpeg 8.0 — not .NET 8), `ghcr.io/jim60105/bgutil-pot:latest`, the yt-dlp `ADD`, `ghcr.io/tarampampam/curl:8.8.0`, and dumb-init.

## 6. Local container validation (includes the OpenAPI spike)

- [x] 6.1 Build the image: `docker build -t soundbuttons:dotnet10 .` (or `docker compose build`); confirm the multi-stage build succeeds on the new images.
- [x] 6.2 Run via `docker compose up` (with the Azurite emulator); confirm the container starts and the `HEALTHCHECK` against `/api/healthz` reports healthy. (This validates the app process and the copied `curl` binary on the non-slim base distro; the FFmpeg/yt-dlp/BgUtil binaries are exercised by the upload smoke test in 6.4, not by the health check.)
- [x] 6.3 **OpenAPI spike (done):** the in-proc `Microsoft.Azure.WebJobs.Extensions.OpenApi 1.6.0` registered **no** OpenAPI endpoints on the isolated worker (every Swagger/OpenAPI route returned 404). Applied design Decision 4 fallback **(a)**: switched to `Microsoft.Azure.Functions.Worker.Extensions.OpenApi 1.6.0`. After the switch the source generator emits the render functions and `GET /api/swagger.json`, `/api/openapi/v3.json`, and `/api/swagger/ui` all return **200** with a valid OpenAPI 3.0.1 document (empty `paths` — the functions carry no `[OpenApiOperation]` attributes, matching existing behavior). `Microsoft.AspNetCore.OpenApi` was not used. This restores a previously-broken capability.
- [x] 6.4 Exercise an end-to-end upload request; confirm the Durable orchestrator + activities run and the audio file and JSON config are written to Blob Storage. **DONE via a no-YouTube smoke test** (the direct file-upload branch bypasses yt-dlp): POSTed a generated 0.3 s WAV as multipart `file` with `directory=test`/`nameZH=SmokeTest`/`nameJP=smoke` (≠`[useSTT]`, so OpenAI is skipped) against the running .NET 10 container, with `AzureStorage` + `AzureWebJobsStorage` pointed at Azurite (started with `--skipApiVersionCheck`). Result: the orchestrator `main-sound-buttons` reached **`Completed` / output=true**; the uploaded WAV was transcoded to `.webm` (validates **Xabe.FFmpeg 6.0.2** + the static FFmpeg binary on the non-slim base) and uploaded to `test/SmokeTest.webm` (validates **Azure.Storage.Blobs 12.28.0** + **Extensions.Storage.Blobs 6.8.1**); `test/test.json` was read, merged (new button added to the `未分類` group) and re-written with a timestamped backup under `test/UploadJson/` (validates in-box **System.Text.Json** + the JSON flow); and the **Durable** orchestration + all three activities (`ProcessAudio` skipped as `TempPath` was pre-set, `UploadAudioToStorageAsync`, `SpeechToTextAsync`, `ProcessJsonFile`) executed on **Extensions.DurableTask 1.16.5** / Worker 2.x. The yt-dlp/YouTube clip branch was not exercised (needs live network) but shares only the post-download path with this test.
- [x] 6.5 Confirm Application Insights / Serilog telemetry initializes at startup without errors.

## 7. Deployment manifests and docs

- [x] 7.1 Review `docker-compose.yml`, `SoundButtons/local.settings.json`, and the Helm chart (`helm/values.yaml`, `helm/templates/backend-deployment.yaml`): confirm `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated` is preserved and that no .NET-version-coupled value needs changing (image is tag `:latest`). **Done:** runtime preserved across compose/local.settings/helm; helm image is `:latest`. Added `--skipApiVersionCheck` to the compose Azurite service (required so the upgraded `Azure.Storage.Blobs 12.28.0` SDK can talk to the local emulator; no effect on real Azure Storage).
- [x] 7.2 Update `README.md` (and any developer-setup notes) to state the .NET 10 SDK as the local prerequisite.

## 8. CI and merge

- [ ] 8.1 Validate CI on the feature branch. The `docker_publish.yml` workflow triggers only on `push` to `master`, tags, and `workflow_dispatch` — it does NOT run on branch push or pull_request. So either trigger it manually via `workflow_dispatch` against the feature branch (`gh workflow run docker_publish.yml --ref <branch>`), or add a temporary PR/branch build, to confirm the multi-stage build succeeds on the .NET 10 base images before merge. Expect no workflow-trigger or image-name change. **PENDING:** awaits commit/push; the equivalent Dockerfile build was validated locally via podman (full multi-stage build succeeds on the .NET 10 base images).
- [x] 8.2 Verify no `net8.0`/`-slim`/`isolated8.0`/`sdk:8.0` or other stale .NET 8 references remain across tracked files, explicitly including `SoundButtons/local.settings.json`, `docker-compose.yml`, `helm/`, and `README.md`. (Do not flag the FFmpeg `static-ffmpeg-upx:8.0` tag — that is FFmpeg 8.0, intentionally retained.) **Done — broadened beyond `.cs/.csproj/Dockerfile/yml`:** also fixed stale TFM refs the first pass missed in tracked IDE/deploy files — `.vscode/settings.json` (`net8.0`→`net10.0` deploy subpath), `.vscode/tasks.json` (`net8.0`→`net10.0` debug cwd), and `SoundButtons/Properties/PublishProfiles/SoundButtonsEastAsia - Zip Deploy.pubxml` (`net6.0`→`net10.0`). `.idea/.../workspace.xml` (`net8.0`) is gitignored/untracked — left as-is. `SoundButtons/local.settings.json` is gitignored (not tracked).
- [ ] 8.3 Open the PR with a summary of the platform upgrade and the OpenAPI-spike outcome; merge once CI is green and the spike passed. **PENDING:** awaits the user's go-ahead to commit/push/open PR.
