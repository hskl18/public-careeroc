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
- Set `demoMode=false`, `allowedEmailDomains`, and the Google secret keys for a real deployment.

The chart is checked with `helm lint` and `kubeconform -strict` in both modes; it has not been installed on a live cluster by this project.
