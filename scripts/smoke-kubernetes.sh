#!/usr/bin/env bash
# Installs the Helm chart on the current Kubernetes context (a kind cluster in CI) with a throwaway
# in-cluster Postgres, waits for every service, and runs scripts/smoke-demo.sh through port-forwards.
# Expects the images careeroc/{web,api,agent-runtime}:$IMAGE_TAG to be loaded into the cluster.
set -euo pipefail
cd "$(dirname "$0")/.."

NAMESPACE=${NAMESPACE:-careeroc-smoke}
IMAGE_TAG=${IMAGE_TAG:-smoke}

kubectl create namespace "$NAMESPACE" --dry-run=client -o yaml | kubectl apply -f -
kubectl -n "$NAMESPACE" apply -f - <<'EOF'
apiVersion: apps/v1
kind: Deployment
metadata: { name: postgres }
spec:
  selector: { matchLabels: { app: postgres } }
  template:
    metadata: { labels: { app: postgres } }
    spec:
      containers:
        - name: postgres
          image: postgres:17-alpine
          env:
            - { name: POSTGRES_DB, value: careeroc }
            - { name: POSTGRES_PASSWORD, value: postgres }
          ports: [{ containerPort: 5432 }]
          readinessProbe:
            exec: { command: [pg_isready, -U, postgres, -d, careeroc] }
---
apiVersion: v1
kind: Service
metadata: { name: postgres }
spec:
  selector: { app: postgres }
  ports: [{ port: 5432 }]
EOF

kubectl -n "$NAMESPACE" create secret generic careeroc --dry-run=client -o yaml \
  --from-literal=DATABASE_URL=postgresql://postgres:postgres@postgres:5432/careeroc \
  --from-literal=CAREEROC_API_AUTH_SECRET="$(openssl rand -hex 32)" \
  --from-literal=CAREEROC_TOKEN_PROTECTION_KEY="$(openssl rand -hex 32)" \
  --from-literal=AGENT_SHARED_SECRET="$(openssl rand -hex 32)" \
  --from-literal=AUTH_SECRET="$(openssl rand -hex 32)" \
  --from-literal=GOOGLE_CLIENT_ID= --from-literal=GOOGLE_CLIENT_SECRET= | kubectl apply -f -
kubectl -n "$NAMESPACE" rollout status deployment/postgres --timeout=180s

if ! helm upgrade --install careeroc deploy/helm/careeroc -n "$NAMESPACE" --wait --timeout 10m \
  --set host=localhost --set images.registry=careeroc --set images.tag="$IMAGE_TAG" --set images.pullPolicy=Never; then
  kubectl -n "$NAMESPACE" get pods -o wide
  for app in web api agent-runtime; do kubectl -n "$NAMESPACE" logs "deployment/$app" --tail=60 || true; done
  exit 1
fi

pids=()
trap 'kill "${pids[@]}" 2>/dev/null || true' EXIT
for forward in web:13000:3000 api:15114:8080 agent-runtime:18123:8123; do
  IFS=: read -r service local_port remote_port <<<"$forward"
  kubectl -n "$NAMESPACE" port-forward "service/$service" "$local_port:$remote_port" >/dev/null &
  pids+=("$!")
done

# The chart serves https://<host>; the web app checks Origin against that, not the port-forward address.
WEB_URL=http://localhost:13000 API_URL=http://localhost:15114 AGENT_URL=http://localhost:18123 \
  SITE_ORIGIN=https://localhost scripts/smoke-demo.sh
