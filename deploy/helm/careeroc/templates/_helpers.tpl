{{- define "careeroc.labels" -}}
app.kubernetes.io/part-of: careeroc
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
{{- end -}}

{{- define "careeroc.publicUrl" -}}
https://{{ .Values.host }}
{{- end -}}

{{/* secretEnv name key: one env entry read from the release secret. */}}
{{- define "careeroc.secretEnv" -}}
- name: {{ index . 0 }}
  valueFrom:
    secretKeyRef:
      name: {{ index . 1 }}
      key: {{ index . 2 }}
{{- end -}}

{{/*
careeroc.env: the environment variables the chart sets for one component, as a YAML list. The
workload template parses it to refuse an extraEnv entry with the same name, because server-side
apply, Helm 4's default, rejects a Deployment with duplicate env names.
*/}}
{{- define "careeroc.env" -}}
{{- $root := .root -}}
{{- $name := .name -}}
{{- $port := .port -}}
{{- $publicUrl := .publicUrl -}}
- name: PORT
  value: {{ $port | quote }}
{{- if eq $name "api" }}
- { name: ASPNETCORE_ENVIRONMENT, value: Production }
- { name: ForwardedHeaders__Enabled, value: "true" }
- { name: Database__ApplyMigrationsOnStartup, value: "true" }
- { name: API_PUBLIC_BASE_URL, value: {{ $publicUrl | quote }} }
- { name: NEXT_PUBLIC_SITE_URL, value: {{ $publicUrl | quote }} }
- { name: ACCESS_ALLOWED_DOMAINS, value: {{ ternary "demo.careeroc.example" $root.Values.allowedEmailDomains $root.Values.demoMode | quote }} }
- { name: ACCESS_ALLOWED_EMAILS, value: {{ ternary "" $root.Values.allowedEmails $root.Values.demoMode | quote }} }
- { name: Demo__Enabled, value: {{ $root.Values.demoMode | quote }} }
- { name: AgentRuntime__Enabled, value: "true" }
- { name: AgentRuntime__BaseUrl, value: "http://agent-runtime:8123" }
- { name: SyncWorker__Enabled, value: {{ not $root.Values.demoMode | quote }} }
{{- if gt (int $root.Values.api.replicas) 1 }}
{{- /* The dashboard cache and its reset on writes live in one pod, so with more than one a refresh
could read another pod's stale copy. */}}
- { name: Dashboard__ResponseCacheSeconds, value: "0" }
{{- end }}
{{- include "careeroc.secretEnv" (list "DATABASE_URL" $root.Values.existingSecret "DATABASE_URL") | nindent 0 }}
{{- include "careeroc.secretEnv" (list "CAREEROC_API_AUTH_SECRET" $root.Values.existingSecret "CAREEROC_API_AUTH_SECRET") | nindent 0 }}
{{- include "careeroc.secretEnv" (list "CAREEROC_TOKEN_PROTECTION_KEY" $root.Values.existingSecret "CAREEROC_TOKEN_PROTECTION_KEY") | nindent 0 }}
{{- include "careeroc.secretEnv" (list "AgentRuntime__SharedSecret" $root.Values.existingSecret "AGENT_SHARED_SECRET") | nindent 0 }}
{{- include "careeroc.secretEnv" (list "Google__ClientId" $root.Values.existingSecret "GOOGLE_CLIENT_ID") | nindent 0 }}
{{- include "careeroc.secretEnv" (list "Google__ClientSecret" $root.Values.existingSecret "GOOGLE_CLIENT_SECRET") | nindent 0 }}
{{- else if eq $name "agent-runtime" }}
- { name: CAREEROC_AGENT_PROVIDER, value: {{ $root.Values.agentProvider | quote }} }
{{- with $root.Values.agentModel }}
- { name: CAREEROC_AGENT_MODEL, value: {{ . | quote }} }
{{- end }}
{{- with $root.Values.agentBaseUrl }}
- { name: CAREEROC_AGENT_BASE_URL, value: {{ . | quote }} }
{{- end }}
- name: CAREEROC_AGENT_API_KEY
  valueFrom:
    secretKeyRef:
      name: {{ $root.Values.existingSecret }}
      key: AGENT_API_KEY
      optional: true
- { name: CAREEROC_AGENT_API_BASE_URL, value: "http://api:8080" }
- { name: CAREEROC_AGENT_PUBLIC_URL, value: {{ $publicUrl | quote }} }
# Optional and separate from DATABASE_URL; without it, paused reviews are kept in memory.
- name: CAREEROC_AGENT_CHECKPOINT_DATABASE_URL
  valueFrom:
    secretKeyRef:
      name: {{ $root.Values.existingSecret }}
      key: AGENT_CHECKPOINT_DATABASE_URL
      optional: true
{{- include "careeroc.secretEnv" (list "CAREEROC_AGENT_SHARED_SECRET" $root.Values.existingSecret "AGENT_SHARED_SECRET") | nindent 0 }}
{{- else }}
- { name: DEMO_MODE, value: {{ $root.Values.demoMode | quote }} }
- { name: AUTH_URL, value: {{ $publicUrl | quote }} }
- { name: NEXT_PUBLIC_SITE_URL, value: {{ $publicUrl | quote }} }
- { name: API_BASE_URL, value: "http://api:8080" }
- { name: API_PUBLIC_BASE_URL, value: {{ $publicUrl | quote }} }
- { name: AGENT_PUBLIC_BASE_URL, value: {{ $publicUrl | quote }} }
- { name: ACCESS_ALLOWED_DOMAINS, value: {{ $root.Values.allowedEmailDomains | quote }} }
- { name: ACCESS_ALLOWED_EMAILS, value: {{ $root.Values.allowedEmails | quote }} }
{{- include "careeroc.secretEnv" (list "AUTH_SECRET" $root.Values.existingSecret "AUTH_SECRET") | nindent 0 }}
{{- include "careeroc.secretEnv" (list "CAREEROC_API_AUTH_SECRET" $root.Values.existingSecret "CAREEROC_API_AUTH_SECRET") | nindent 0 }}
{{- include "careeroc.secretEnv" (list "AUTH_GOOGLE_ID" $root.Values.existingSecret "GOOGLE_CLIENT_ID") | nindent 0 }}
{{- include "careeroc.secretEnv" (list "AUTH_GOOGLE_SECRET" $root.Values.existingSecret "GOOGLE_CLIENT_SECRET") | nindent 0 }}
{{- end }}
{{- end -}}
