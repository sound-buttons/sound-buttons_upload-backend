# Sound Buttons — Upload Backend

[![CodeQL](https://github.com/sound-buttons/sound-buttons_upload-backend/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/sound-buttons/sound-buttons_upload-backend/actions/workflows/github-code-scanning/codeql)
[![CodeFactor](https://www.codefactor.io/repository/github/sound-buttons/sound-buttons_upload-backend/badge)](https://www.codefactor.io/repository/github/sound-buttons/sound-buttons_upload-backend) [![docker_publish](https://github.com/sound-buttons/sound-buttons_upload-backend/actions/workflows/docker_publish.yml/badge.svg)](https://github.com/sound-buttons/sound-buttons_upload-backend/actions/workflows/docker_publish.yml) [![test](https://github.com/sound-buttons/sound-buttons_upload-backend/actions/workflows/test.yml/badge.svg)](https://github.com/sound-buttons/sound-buttons_upload-backend/actions/workflows/test.yml) [![codecov](https://codecov.io/gh/sound-buttons/sound-buttons_upload-backend/branch/master/graph/badge.svg)](https://codecov.io/gh/sound-buttons/sound-buttons_upload-backend)

The audio-ingestion backend for [**Sound Buttons**](https://sound-buttons.click) — a fan-made
soundboard for VTuber clips. This service accepts a user-submitted clip (a YouTube video id,
a YouTube/Twitch clip URL, or a direct file upload), downloads and encodes the audio, runs
optional speech-to-text, publishes the result to Azure Blob Storage, and updates the
per-character button configuration.

It is an **Azure Functions v4** app running on the **.NET 10 isolated worker**, orchestrated
with **Durable Functions**.

## How it works

A single HTTP trigger starts a Durable orchestration that fans out to activity functions:

1. **`sound-buttons`** (HTTP trigger) — validates the `multipart/form-data` upload, resolves
   the source (YouTube id / YouTube clip / Twitch clip / direct file), and schedules the
   orchestrator.
2. **`ProcessAudioAsync`** — downloads the source with **yt-dlp** (`YoutubeDLSharp`), then cuts
   and transcodes it to audio-only WebM with **FFmpeg** (`Xabe.FFmpeg`).
3. **`SpeechToTextAsync`** — optional transcription via the **OpenAI** API (sentinel-gated).
4. **`UploadAudioToStorageAsync`** — uploads the encoded audio to **Azure Blob Storage**.
5. **`ProcessJsonFile`** — updates the character's button-config JSON (read-modify-write).

A `healthz` HTTP function provides a health probe at `/api/healthz`.

## Project structure

| Path | Description |
| ---- | ----------- |
| `SoundButtons/Functions/` | Function entry points (HTTP trigger, orchestrator, activities). |
| `SoundButtons/Services/` | `ProcessAudioService` (yt-dlp + FFmpeg) and `OpenAiService` (transcription), behind interfaces. |
| `SoundButtons/Helper/` | Tool discovery (`YoutubeDLHelper`) and file helpers. |
| `SoundButtons/Models/` | Data models (`Button`, `Source`, `Text`, `JsonRoot`, …). |
| `SoundButtons.Tests/` | xUnit unit + integration tests. |
| `helm/` | Kubernetes Helm chart. |
| `Dockerfile` | Multi-stage container build (incl. `test`/`report` stages). |
| `openspec/` | Spec-driven specs and change proposals. |

## Development

Prerequisites:

- **.NET 10 SDK** (`mcr.microsoft.com/dotnet/sdk:10.0` if building in a container)
- **Docker / Podman** for the containerized runtime and the Azurite storage emulator

Run locally with the bundled emulator:

```bash
docker compose up --build
```

The Functions host is published on `http://localhost:7071` (health check at `/api/healthz`).
The Compose file starts an [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite)
emulator so the Durable Functions runtime and blob uploads work without a real Azure account.

### Configuration

Configuration is supplied via environment variables (locally through Compose or a `.env`
file; in production through the Helm chart):

| Variable | Purpose |
| -------- | ------- |
| `AzureWebJobsStorage` | Azure Functions / Durable Functions runtime storage. |
| `AzureStorage` | Application Blob Storage for audio + JSON publishing. |
| `OpenAI_ApiKey` | Enables the optional speech-to-text step. |
| `Seq_ServerUrl`, `Seq_ApiKey` | Optional [Seq](https://datalust.co/seq) log sink. |

Never commit secrets; `local.settings.json` is excluded from publish output.

## Testing

The test suite runs inside the Dockerfile `test` stage, which enforces a **≥ 85 % line and
branch** coverage gate (via `coverlet.msbuild`) and provides the real `ffmpeg`/`yt-dlp`
binaries used by the integration tests:

```bash
# Run the suite + coverage gate
docker build --target test -f Dockerfile .

# Export the Cobertura + TRX reports to ./out/testresults/
docker build --target report --output type=local,dest=./out -f Dockerfile .
```

CI (`.github/workflows/test.yml`) runs the same stage on every pull request and push to
`master`, builds the production image, uploads coverage to Codecov, and runs infrastructure
conformance checks. See [`AGENTS.md`](AGENTS.md) for contributor and agent guidance.

## Related repositories

| Repository | Purpose |
| ---------- | ------- |
| [sound-buttons](https://github.com/sound-buttons/sound-buttons) | Frontend web app. |
| [sound-buttons_configs](https://github.com/sound-buttons/sound-buttons_configs) | JSON button configuration data. |
| sound-buttons_upload-backend | Audio upload backend (this repo). |

## LICENSE

> Use Xabe.FFmpeg with the [License Agreement](https://ffmpeg.xabe.net/license.html) under non-commercial use.  
> Use YoutubeDLSharp under the [BSD 3-Clause License](https://github.com/Bluegrams/YoutubeDLSharp/blob/master/LICENSE.txt).  
> Use yt-dlp under the [Unlicensed License](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE).

<img src="https://github.com/user-attachments/assets/1ed19957-d1d2-4b6f-85ee-cc9a77ab51b3" alt="agplv3" width="300" />

[GNU AFFERO GENERAL PUBLIC LICENSE Version 3](LICENSE)

Copyright (C) 2021 Jim Chen <Jim@ChenJ.im>.

This program is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with this program. If not, see <https://www.gnu.org/licenses/>.
