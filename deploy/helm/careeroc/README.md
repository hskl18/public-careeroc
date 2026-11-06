# careeroc Helm chart

Runs the web app, API, and agent runtime on any Kubernetes cluster (GKE, AKS, EKS, or self-managed) behind one Ingress host.

```bash
kubectl create namespace careeroc
kubectl -n careeroc create secret generic careeroc \
  --from-literal=DATABASE_URL='postgresql://user:password@host:5432/careeroc?sslmode=require' \
  --from-literal=CAREEROC_API_AUTH_SECRET="$(openssl rand -hex 32)" \
  --from-literal=CAREEROC_TOKEN_PROTECTION_KEY="$(openssl rand -hex 32)" \
  --from-literal=AGENT_SHARED_SECRET="$(openssl rand -hex 32)" \
  --from-literal=AUTH_SECRET="$(openssl rand -hex 32)" \
  --from-literal=GOOGLE_CLIENT_ID='' --from-literal=GOOGLE_CLIENT_SECRET=''
helm install careeroc deploy/helm/careeroc -n careeroc --set host=careeroc.example.com
```

- CI installs this chart on a kind cluster and runs the demo smoke test through it; see `scripts/smoke-kubernetes.sh` for a working minimal install.
- Postgres is external (Cloud SQL, Azure Database for PostgreSQL, RDS, or an operator such as CloudNativePG); pass its URL in `DATABASE_URL`.
- The agent runtime never receives `DATABASE_URL`.
  To keep paused reviews across restarts, create its own database and role with [agent-checkpoints.sql](../../postgres/agent-checkpoints.sql) and add `AGENT_CHECKPOINT_DATABASE_URL` to the secret; without it, checkpoints live in memory.
- TLS is required because the web app refuses plain HTTP in production; point `ingress.tlsSecretName` at a certificate, for example one issued by cert-manager.
- Images default to `ghcr.io/hskl18/public-careeroc/<service>`, published by `.github/workflows/images.yml` on version tags.
- Services use fixed short names (`api`, `agent-runtime`, `web`) that match the other deployments, so install one release per namespace.
- Set `demoMode=false`, the Google secret keys, and `allowedEmails` or `allowedEmailDomains` for a real deployment; the install fails without one of the two.
  Use `allowedEmails` for personal addresses, because `allowedEmailDomains=gmail.com` admits every Gmail account.
- `ingress.className` defaults to `nginx`, with read and send timeouts of 130 seconds, because a review runs up to 100 seconds behind one request; on another controller, set its own timeout.
- `web.extraEnv`, `api.extraEnv`, and `agentRuntime.extraEnv` add environment variables, such as `OTEL_EXPORTER_OTLP_ENDPOINT` or `Ollama__*` for the optional model pass on the API.
  They only add names: the install fails if one repeats a variable the chart sets, which server-side apply would reject.
- Pods read the secret only when they start, so after changing it run `kubectl rollout restart deployment/web deployment/api deployment/agent-runtime`.
- `networkPolicy.enabled=true` lets each service accept traffic only from the ingress controller's namespace and the release's own callers; set `networkPolicy.ingressControllerNamespaceLabels` to match your controller's namespace.
  It restricts ingress only, and needs a CNI that enforces NetworkPolicy; the kind smoke test installs with it on.
  Add `networkPolicy.extraFrom` entries, such as an `ipBlock`, for a load balancer that sends straight to pods or a hostNetwork ingress controller.

The chart is checked with `helm lint` and with `kubeconform -strict` in demo mode, real mode, and with the network policy on; it has not been installed on a live cluster by this project.
