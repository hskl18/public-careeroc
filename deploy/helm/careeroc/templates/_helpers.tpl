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
