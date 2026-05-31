# blob-storage-publishing Specification

## Purpose

Defines how a processed sound button is persisted to Azure Blob Storage: uploading
the encoded audio file and updating the per-directory button-catalog JSON that the
front-end consumes. Implemented in `SoundButtons/Functions/UploadAudioToStorage.cs`
and `SoundButtons/Functions/ProcessJson.cs`, both using the named
`sound-buttons` `BlobServiceClient` and `sound-buttons` container. The blob client
reads its connection string from the `AzureStorage` environment variable
(distinct from the Durable runtime's `AzureWebJobsStorage`).

## Requirements

### Requirement: Audio blob upload

The service SHALL ensure the `sound-buttons` container exists and upload the
encoded audio to `<directory>/<filename><extension>` with the `audio/webm`
content type.

#### Scenario: Audio uploaded with content type

- **GIVEN** a processed audio temp file and a target `directory`/`filename`
- **WHEN** `UploadAudioToStorageAsync` runs
- **THEN** the container is created if it does not exist
- **AND** the audio is uploaded to `<directory>/<filename><extension>` with `Content-Type: audio/webm`

### Requirement: Filename collision avoidance

If a blob already exists at the target path, the service SHALL append a uniqueness
suffix (the current `DateTime.Now.Ticks`) to the filename before uploading, and
SHALL record the final filename back onto the request so downstream JSON update
uses the same name.

#### Scenario: Existing blob gets a unique suffix

- **GIVEN** a target blob path that already exists
- **WHEN** the upload runs
- **THEN** the filename is suffixed with `_<ticks>` and uploaded to the new path
- **AND** the request's `Filename` is updated to the suffixed value

### Requirement: Source IP recorded as blob metadata

When the request IP value is non-null, the service SHALL store it as the blob
metadata key `sourceIp` on the uploaded audio blob. Because the submission handler
substitutes an empty string when the `X-Forwarded-For` header is absent, the
metadata may be set to an empty string rather than omitted.

#### Scenario: IP stored in metadata

- **GIVEN** a non-null request IP (possibly the empty string)
- **WHEN** the audio blob is uploaded
- **THEN** the blob metadata contains `sourceIp` set to that IP value

### Requirement: Button-catalog JSON is read before update

The service SHALL read the existing catalog blob at `<directory>/<directory>.json`
before modifying it. If that blob does not exist, the service SHALL log a critical
error and abort the JSON update without creating a new catalog. If the blob
content deserializes to a null catalog, the service SHALL likewise log a critical
error and abort. (Malformed JSON that raises a deserialization exception is not
caught by the current implementation and propagates as a failure.)

#### Scenario: Missing catalog aborts update

- **GIVEN** no `<directory>/<directory>.json` blob exists
- **WHEN** `ProcessJsonFile` runs
- **THEN** it logs a critical error and returns without writing a catalog

#### Scenario: Null-deserialized catalog aborts update

- **GIVEN** a catalog blob whose content deserializes to a null catalog
- **WHEN** `ProcessJsonFile` runs
- **THEN** it logs a critical error and returns without writing a catalog

### Requirement: New button appended to the correct group

The service SHALL add the new button to the button group whose name matches the
submission `group` (by Traditional Chinese or Japanese name). When no matching
group exists, it SHALL create a new group named by the submission `group` whose
`baseRoute` is the directory's public blob URL
(`https://soundbuttons.blob.core.windows.net/sound-buttons/<directory>/`);
existing groups retain their current `baseRoute`. When a matching group lacks a
Japanese name, the group's Japanese name SHALL be backfilled from its Traditional
Chinese name. The button SHALL carry the filename, bilingual text
(`nameZH`/`nameJP`), volume, and source; individual buttons are not assigned a
`baseRoute`.

#### Scenario: Append to existing group

- **GIVEN** a catalog containing a group matching the submission `group`
- **WHEN** the catalog is updated
- **THEN** the new button is appended to that group's `buttons`

#### Scenario: Create missing group

- **GIVEN** a catalog with no group matching the submission `group`
- **WHEN** the catalog is updated
- **THEN** a new group is created with that name and `baseRoute` set to the directory's public blob URL, and the button is added to it

### Requirement: Injection-safe source field

The service SHALL URL-encode the source `videoId` before writing it into the
catalog JSON to prevent script injection through the stored value.

#### Scenario: videoId is URL-encoded

- **GIVEN** a source `videoId`
- **WHEN** the button is written to the catalog
- **THEN** the stored `videoId` is URL-encoded

### Requirement: Catalog write with timestamped backup

The service SHALL write the updated catalog back to the canonical
`<directory>/<directory>.json` blob and also write a timestamped backup copy under
`<directory>/UploadJson/<yyyy-MM-dd-HH-mm>.json`, both with `application/json`
content type. The two writes are issued concurrently (`Task.WhenAll`) and are not
performed as a single atomic transaction.

#### Scenario: Catalog and backup written

- **GIVEN** an updated catalog
- **WHEN** `ProcessJsonFile` writes the result
- **THEN** the canonical `<directory>/<directory>.json` blob is overwritten with the new content
- **AND** a backup blob `<directory>/UploadJson/<yyyy-MM-dd-HH-mm>.json` is written
- **AND** both blobs use `Content-Type: application/json`
