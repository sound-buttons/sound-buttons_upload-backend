## 1. Helm value

- [x] 1.1 Add `automountServiceAccountToken: false` under `kubernetes.backend` in `helm/values.yaml`, with a comment explaining the backend never calls the Kubernetes API and that operators can set it to `true` to re-enable the token mount.

## 2. Deployment template

- [x] 2.1 In `helm/templates/backend-deployment.yaml`, add `automountServiceAccountToken` to the pod template `spec` (sibling of `containers`), rendering it with a direct value reference (`{{ .Values.kubernetes.backend.automountServiceAccountToken }}`) so the key is always emitted and never falls back to the Kubernetes cluster default of `true`. (Note: the `dig` helper fails on `.Values` — it is a `chartutil.Values`, not a plain map.)

## 3. Validation

- [x] 3.1 Run `helm lint helm/` and confirm it passes.
- [x] 3.2 Run `helm template helm/` with default values and confirm the rendered backend Deployment pod spec contains `automountServiceAccountToken: false`.
- [x] 3.3 Run `helm template helm/ --set kubernetes.backend.automountServiceAccountToken=true` and confirm the rendered pod spec contains `automountServiceAccountToken: true`.
- [x] 3.4 Confirm no other template, the service, ingress, or the `azurite-emulator` sidecar definition changed, and that the liveness probe / ports are untouched.
- [ ] 3.5 (Optional, requires a cluster) Deploy the chart and verify the running pod has no `/var/run/secrets/kubernetes.io/serviceaccount/` mount and that `/api/healthz` still returns healthy — confirming the disabled token has no functional impact.

## 4. Commit

- [x] 4.1 Commit the chart changes with a conventional message and the Co-authored-by trailer.
