## Why

The backend workload never talks to the Kubernetes API server, yet its Helm
Deployment leaves `automountServiceAccountToken` unset, so Kubernetes mounts a
real ServiceAccount token into every pod by default. That token is an
unnecessary authenticated credential sitting on disk inside the container — if
the process (or a bundled dependency such as `yt-dlp`/FFmpeg handling untrusted
remote input) is compromised, it expands the blast radius (cluster API discovery
today, and anything granted should RBAC later be attached to the `default`
ServiceAccount). Removing an unused credential is a cheap, high-value hardening
step.

## What Changes

- Disable ServiceAccount token automounting for the backend pod by default by
  setting `automountServiceAccountToken: false` on the Deployment pod template
  in `helm/templates/backend-deployment.yaml`.
- Add an `automountServiceAccountToken` value (default `false`) under
  `kubernetes.backend` in `helm/values.yaml` so the behavior is configurable for
  the rare operator who needs the token, without editing templates.
- Apply the setting at the **pod** level (not by editing the `default`
  ServiceAccount) so the chart stays self-contained and does not mutate
  cluster-shared objects.

## Capabilities

### New Capabilities
- `kubernetes-deployment-security`: Defines the pod-level security posture the
  Helm chart must enforce for the backend workload, beginning with disabling
  unused ServiceAccount API-token automounting.

### Modified Capabilities
<!-- None. The existing `dotnet-runtime-platform` spec governs runtime/image/CI
     concerns; the pod security posture is new behavior not covered there. -->

## Impact

- **Helm chart**: `helm/templates/backend-deployment.yaml` (pod spec gains
  `automountServiceAccountToken`), `helm/values.yaml` (new
  `kubernetes.backend.automountServiceAccountToken` key, default `false`).
- **Runtime behavior**: No functional change — the backend (Azure Functions
  isolated worker: audio processing, Blob Storage, Durable orchestration) makes
  no in-cluster Kubernetes API calls, so the token was never used. Liveness
  probe, networking, storage, and the second `azurite-emulator` container are
  unaffected.
- **Operators**: Anyone who deliberately needs the token can re-enable it via
  `--set kubernetes.backend.automountServiceAccountToken=true`.
- **No code, image, API, or CI changes.**
