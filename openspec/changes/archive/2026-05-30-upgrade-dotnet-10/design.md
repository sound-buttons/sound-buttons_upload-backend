## Context

The `sound-buttons_upload-backend` is an Azure Functions v4 app running on the **.NET 8 isolated worker**. It ingests an upload request over HTTP, then uses **Durable Functions** (modern isolated API: `DurableTaskClient` / `TaskOrchestrationContext`) to clip YouTube audio with **yt-dlp** + **FFmpeg** (`Xabe.FFmpeg`), optionally transcribe via OpenAI, upload the result and an updated JSON config to **Azure Blob Storage**, and expose an OpenAPI document. It is shipped as a multi-arch-aware container (`Dockerfile`, built `linux/amd64` in CI via `docker_publish.yml`) and deployed via a Helm chart.

Current platform anchors:
- `SoundButtons.csproj`: `<TargetFramework>net8.0</TargetFramework>`, `AzureFunctionsVersion v4`, `FrameworkReference Microsoft.AspNetCore.App`, Worker `1.23.0` / Worker.Sdk `1.17.4`.
- `Dockerfile`: build `mcr.microsoft.com/dotnet/sdk:8.0`; runtime `mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0-slim`.
- `Program.cs`: `HostBuilder().ConfigureFunctionsWebApplication()`, a `.NET`/`8.0` User-Agent string, `IOpenApiConfigurationOptions` from the WebJobs OpenAPI extension, `AddScoped` services.

.NET 10 is the current LTS (GA Nov 2025). Moving to it keeps the app on a supported, patched runtime and base image.

## Goals / Non-Goals

**Goals:**
- Retarget the project to `net10.0` and build/run it on the Azure Functions v4 .NET 10 isolated worker.
- Upgrade the Worker SDK/packages to the **mandatory** 2.x line required for .NET 10, plus the framework-coupled and extension packages, to versions confirmed available on NuGet.
- Update the `Dockerfile` build and runtime images to .NET 10, using a base tag that **actually exists** in the registry.
- Keep all externally observable behavior identical (HTTP contract, Durable flow, Blob outputs).
- Verify the CI image build/publish succeeds.
- Update developer-facing docs.

**Non-Goals:**
- No functional/feature changes, no API redesign, no new endpoints.
- Not adopting the preview `Azure.Functions.Sdk` MSBuild SDK (stay on `Microsoft.Azure.Functions.Worker.Sdk`).
- Not migrating telemetry from the classic Application Insights SDK to the OpenTelemetry exporter (noted as a future option, out of scope here).
- Not refactoring `HostBuilder` to the new `FunctionsApplication.CreateBuilder` pattern (the existing pattern remains supported).
- No changes to FFmpeg/yt-dlp/POT tooling versions.

## Decisions

### Decision 1: Target `net10.0` and cross to the Worker 2.x line
Targeting `net10.0` on the Functions v4 host **requires** `Microsoft.Azure.Functions.Worker ≥ 2.50.0` and `Microsoft.Azure.Functions.Worker.Sdk ≥ 2.0.5` (Microsoft Learn, isolated-process guide). This is not optional, so the upgrade necessarily crosses the Worker `1.x → 2.x` boundary, which pulls the HTTP/ASP.NET Core extension to its `2.x` line as well.

Chosen target versions (latest stable verified on NuGet at proposal time):

| Package | Current | Target |
|---|---|---|
| `Microsoft.Azure.Functions.Worker` | 1.23.0 | **2.52.0** |
| `Microsoft.Azure.Functions.Worker.Sdk` | 1.17.4 | **2.0.7** |
| `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore` | 1.3.2 | **2.1.0** |
| `Microsoft.Azure.Functions.Worker.Extensions.Storage.Blobs` | 6.6.0 | **6.8.1** |
| `Microsoft.Azure.Functions.Worker.Extensions.DurableTask` | 1.1.5 | **1.16.5** |
| `Microsoft.Azure.Functions.Worker.ApplicationInsights` | 1.4.0 | **2.50.0** |
| `Microsoft.ApplicationInsights.WorkerService` | 2.22.0 | **3.1.2** |
| `Microsoft.Azure.WebJobs.Extensions.OpenApi` | 1.5.1 | **replaced** with `Microsoft.Azure.Functions.Worker.Extensions.OpenApi` 1.6.0 (see Decision 4) |
| `Microsoft.Extensions.Configuration.UserSecrets` | 8.0.0 | **10.0.x** |
| `Azure.Storage.Blobs` | 12.21.2 | **12.28.0** |
| `Serilog` | 4.0.1 | **4.3.1** |
| `Serilog.AspNetCore` | 8.0.2 | **10.0.0** |
| `Serilog.Sinks.Console` | 6.0.0 | **6.1.1** |
| `Serilog.Sinks.Seq` | 8.0.0 | **9.1.0** |
| `Xabe.FFmpeg` | 5.2.6 | **6.0.2** |
| `YoutubeDLSharp` | 1.1.2 | **1.2.0** |
| `System.Text.Json` | 9.0.10 | **10.0.x** (or drop the explicit ref — in-box for net10.0) |

Exact patch versions SHALL be re-confirmed against NuGet at implementation time (the `tasks.md` includes this verification step). `System.Net.Http` and `System.Text.RegularExpressions` explicit references are legacy pins that should be dropped if no longer needed on net10.0.

**Required vs. optional bumps:** Only the **Worker core/SDK (2.x)** and the **framework-coupled** packages (`...Http.AspNetCore` 2.x, `Microsoft.Extensions.*`, the ASP.NET Core shared framework, and `System.Text.Json` if pinned) are *strictly required* to compile and run on net10.0. The remaining bumps (`Xabe.FFmpeg 5→6`, `YoutubeDLSharp`, the Serilog stack, `Azure.Storage.Blobs`, the Application Insights packages, the DurableTask extension) are *recommended currency updates* but not all strictly necessary. To honor the "no observable behavior change" goal and keep the debugging surface small, the tasks apply the **required** set first and validate, then apply the **optional** currency bumps as a separate, independently-revertable step — so a regression can be bisected to the right group. Any optional bump that misbehaves can be deferred to its own follow-up change without blocking the runtime upgrade.

_Alternative considered:_ stay on .NET 8 and only bump patch versions — rejected; it does not meet the goal and .NET 8 base-image/runtime support is winding down relative to the .NET 10 LTS.

### Decision 2: Runtime base image — use `4-dotnet-isolated10.0` (NOT a `-slim` tag)
**Verified finding:** the registry publishes **no `-slim` variant for the .NET 10 isolated line.** `docker manifest inspect mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0-slim` → not found, while `4-dotnet-isolated10.0` and `4-dotnet-isolated10.0-azurelinux3` exist. The current `Dockerfile` uses `...8.0-slim`, so a naive find-replace to `...10.0-slim` would break the build.

Decision: switch the runtime base to **`mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0`**. The build stage moves to **`mcr.microsoft.com/dotnet/sdk:10.0`**.

_Alternatives considered:_ `4-dotnet-isolated10.0-azurelinux3` (Azure Linux base — smaller/CBL-Mariner, but changes the distro and may affect the `apt`/glibc assumptions of the copied static binaries; defer as a separate optimization) and the full `-appservice` tag (carries App Service-specific tooling not needed here). The plain `4-dotnet-isolated10.0` is the closest behavioral analog to the current Debian-based slim image and the lowest-risk choice. Image-size impact (losing `-slim`) is accepted; it can be revisited via the Azure Linux variant later.

### Decision 3: The FFmpeg `:8.0` tag is FFmpeg's version — leave it alone
`COPY --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0` and `.../bgutil-pot`, `.../curl:8.8.0`, yt-dlp, and dumb-init are tool sources whose tags are unrelated to .NET. The `8.0` here is **FFmpeg 8.0**, a coincidence with .NET 8 that must not be "upgraded." Only the two `dotnet` `FROM` lines change.

### Decision 4: Keep the OpenAPI extension but treat it as the primary risk
The app references the **in-process/WebJobs-family** package `Microsoft.Azure.WebJobs.Extensions.OpenApi` (v1.5.1) and configures `IOpenApiConfigurationOptions` from `...WebJobs.Extensions.OpenApi.Core` in `Program.cs`, even though it runs on the **isolated** worker. There are two distinct package families to keep straight:
- `Microsoft.Azure.WebJobs.Extensions.OpenApi` — the package **currently referenced**; primarily the in-process variant. Latest stable `1.6.0` (a swagger-UI-only bump). Its use from an isolated app is itself a smell to validate.
- `Microsoft.Azure.Functions.Worker.Extensions.OpenApi` — the **isolated-worker** variant. Latest stable `1.6.0`; `2.0.0-preview2` still only targets `net6.0/net7.0/netstandard2.0` and pins `Worker.Core 1.8.0`.

Neither family advertises .NET 10 / Worker 2.x support, and both are largely dormant. NuGet will unify the transitive `Worker.Core` to 2.x, but this is untested by the package author.

Decision: keep the current package, bump to `1.6.0`, and **prove the OpenAPI/Swagger endpoint at build/runtime via a container spike** (the tasks include explicitly fetching the document/UI). If it fails against Worker 2.x / net10.0, evaluate fallbacks **in this order**: (a) switch to the isolated-worker `Microsoft.Azure.Functions.Worker.Extensions.OpenApi` package (the architecturally-correct package for this app) and re-test; (b) hold/pin a working version combination; (c) try `2.0.0-preview2`; (d) replace the auto-generated document with a manually-maintained/static OpenAPI document served from a dedicated endpoint; or, only as a last resort, (e) accept OpenAPI degradation/removal as a documented, scoped behavior change.

**Outcome (resolved by the implementation spike):** The in-process `Microsoft.Azure.WebJobs.Extensions.OpenApi` package did **not** register any OpenAPI endpoints on the isolated worker — only the app's own functions were discovered and every OpenAPI/Swagger route returned **404**. Fallback **(a)** was therefore applied: the reference was switched to the isolated-worker `Microsoft.Azure.Functions.Worker.Extensions.OpenApi 1.6.0`. With that package the source generator emits the `RenderSwaggerDocument` / `RenderOpenApiDocument` / `RenderSwaggerUI` / `RenderOAuth2Redirect` functions, the build stays at **0 warnings / 0 errors** (NuGet unifies the transitive `Worker.Core` to 2.x), and at runtime `GET /api/swagger.json`, `/api/openapi/v3.json`, and `/api/swagger/ui` all return **200** with a valid OpenAPI 3.0.1 document. The document's `paths` are empty because the functions carry no `[OpenApiOperation]` attributes today — this matches existing behavior and is not a regression. `Program.cs` is unchanged: the isolated package consumes the same `IOpenApiConfigurationOptions` registration from the shared `...WebJobs.Extensions.OpenApi.Core` namespaces. Net effect: this upgrade **restores** a previously-broken OpenAPI surface rather than degrading it.

Note: `Microsoft.AspNetCore.OpenApi` is **not** a drop-in replacement here — it generates documents from ASP.NET Core endpoint metadata, not from Azure Functions `HttpTrigger` metadata, so `ConfigureFunctionsWebApplication()` alone would not surface the Functions to `MapOpenApi()`. It is therefore explicitly excluded as a fallback unless the HTTP surface is first re-expressed as ASP.NET Core endpoints (out of scope).

### Decision 5: Audit, but expect minimal impact from, the Worker 2.x behavioral breaks
Source review shows the app is largely insulated from the documented 2.x breaks:
- No `HttpResponseData.WriteAsJsonAsync()` usage (uses `CreateResponse` + `WriteString`), so the "no longer forces 200 OK" change does not bite.
- No `ILoggerExtensions`/`LogMetric` usage (the rename to `FunctionsLoggerExtensions` is a no-op here).
- No batch/collection triggers, so `IncludeEmptyEntriesInMessagePayload` default-on is irrelevant.
- DI registrations are a singleton **factory** for `IOpenApiConfigurationOptions` plus two `AddScoped` services — no singleton captures a scoped service, so the now-default DI scope validation should pass.
- `EnableUserCodeException` default-on only improves error surfacing.

These are validated rather than assumed (tasks include a runtime smoke test).

## Risks / Trade-offs

- **OpenAPI extension incompatibility with Worker 2.x / net10.0** → Mitigation: container spike that exercises the OpenAPI/Swagger endpoint; documented fallbacks in priority order (switch to the isolated-worker `Microsoft.Azure.Functions.Worker.Extensions.OpenApi` package, hold/pin a working combination, try `2.0.0-preview2`, or serve a manually-maintained static document). `Microsoft.AspNetCore.OpenApi` is explicitly **not** a viable drop-in (see Decision 4). This is the highest-likelihood failure point.
- **No `-slim` .NET 10 base image → larger image / different layer set** → Mitigation: use `4-dotnet-isolated10.0`; accept the size change now, with `-azurelinux3` as a documented future size optimization. Confirm the copied static binaries (FFmpeg/curl/dumb-init) still run on the chosen base distro via the existing `HEALTHCHECK`.
- **Large multi-major dependency jumps** (DurableTask 1.1.5→1.16.5, Serilog.AspNetCore 8→10, App Insights 2→3, Xabe.FFmpeg 5→6) could surface API or behavioral changes → Mitigation: build + runtime smoke test of the full upload→clip→upload flow; review each major's changelog during implementation; bump incrementally if a specific package misbehaves.
- **Application Insights 3.x / Worker.ApplicationInsights 2.x interop on net10.0 not jointly documented** → Mitigation: validate telemetry initializes at startup; the classic SDK path remains supported. OpenTelemetry migration is a separate future change.
- **Auto-generated `WorkerExtensions` project TFM** (currently net6.0) under Worker.Sdk 2.0.7 is not documented → Mitigation: it is regenerated by the SDK; a clean `obj/` rebuild plus successful publish is the acceptance signal. Do not hand-edit generated output.
- **Exact patch versions drift** between proposal and implementation → Mitigation: tasks re-verify latest stable on NuGet before pinning.

## Migration Plan

1. **Branch** off `master`.
2. **csproj**: set `net10.0`; update package versions per Decision 1 (re-verify latest stable); drop obsolete `System.Net.Http`/`System.Text.RegularExpressions`/explicit `System.Text.Json` pins if unneeded.
3. **Clean restore/build** on the .NET 10 SDK (`rm -rf obj bin`); fix any compile breaks from the 2.x worker surface.
4. **Program.cs**: update the `.NET` User-Agent product version `8.0` → `10.0`.
5. **Dockerfile**: build image → `dotnet/sdk:10.0`; runtime base → `azure-functions/dotnet-isolated:4-dotnet-isolated10.0`; leave FFmpeg/yt-dlp/POT/curl/dumb-init untouched.
6. **Local container build + run** (`docker compose` with Azurite); hit `/api/healthz` and the OpenAPI endpoint; exercise an end-to-end upload to validate the Durable flow and Blob output.
7. **Helm/compose**: confirm `dotnet-isolated` runtime and image refs; no version-coupled change expected.
8. **Docs**: README / dev-setup → .NET 10 SDK prerequisite.
9. **CI**: push branch; confirm `docker_publish.yml` builds & publishes green.
10. **Merge** once the spike (esp. OpenAPI) passes.

**Rollback:** revert the branch/commit; the previous `net8.0` image tag remains available in `ghcr.io/sound-buttons/backend` history, so redeploying the prior image immediately restores service.

## Open Questions

- Does the currently-referenced `Microsoft.Azure.WebJobs.Extensions.OpenApi 1.6.0` actually function against Worker 2.52.0 on net10.0, or must we switch to the isolated-worker `Microsoft.Azure.Functions.Worker.Extensions.OpenApi` package (or another Decision 4 fallback)? — **Resolved:** the in-proc package registered **no** OpenAPI endpoints on the isolated worker (all routes 404), so fallback (a) was applied — switched to `Microsoft.Azure.Functions.Worker.Extensions.OpenApi 1.6.0`, which builds clean and serves the OpenAPI/Swagger endpoints (200). See Decision 4 Outcome.
- Should we take the smaller `-azurelinux3` base now, or keep the Debian-based `4-dotnet-isolated10.0` for minimal change? — default to the latter; revisit as a follow-up optimization.
- Can the legacy explicit `System.Net.Http 4.3.4` / `System.Text.RegularExpressions 4.3.1` references be removed safely on net10.0? — verify during the clean build.
