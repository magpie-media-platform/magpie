{{- define "magpie.fullname" -}}
{{- if contains .Chart.Name .Release.Name -}}
{{- .Release.Name | trunc 52 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name .Chart.Name | trunc 52 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{- define "magpie.selectorLabels" -}}
app.kubernetes.io/name: {{ .Chart.Name }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "magpie.labels" -}}
{{ include "magpie.selectorLabels" . }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
{{- end -}}

{{- define "magpie.image" -}}
{{- $defaultTag := .Chart.AppVersion -}}
{{- if eq .Values.database.provider "postgresql" -}}
{{- $defaultTag = printf "%s-postgresql" .Chart.AppVersion -}}
{{- end -}}
{{- printf "%s:%s" .Values.image.repository (.Values.image.tag | default $defaultTag | toString) -}}
{{- end -}}

{{- define "magpie.mode" -}}
{{- ternary "cluster" "single-node" .Values.cluster.enabled -}}
{{- end -}}

{{- define "magpie.validate" -}}
{{- if and (eq .Chart.AppVersion "0.0.0") (not .Values.image.tag) -}}
{{- fail "The chart appVersion 0.0.0 is a placeholder, and no image has this tag. Set image.tag, or install the chart package that CI makes." -}}
{{- end -}}
{{- $mode := include "magpie.mode" . -}}
{{- $installed := lookup "apps/v1" "StatefulSet" .Release.Namespace (include "magpie.fullname" .) | dig "metadata" "annotations" "magpie/mode" $mode -}}
{{- if and (ne $installed $mode) (ne .Values.modeChange.to $mode) -}}
{{- fail (printf "This changes the mode from %s to %s. Magpie then does not find its keys, extensions and backups, and makes new keys. Do the procedure \"Change the mode\" in https://github.com/magpie-media-platform/magpie/blob/main/docs/installation/helm.md, then set modeChange.to to %s." $installed $mode $mode) -}}
{{- end -}}
{{- if and .Values.modeChange.to (ne (toString .Values.modeChange.to) $mode) -}}
{{- fail (printf "modeChange.to must be the new mode, %s, not %q." $mode (toString .Values.modeChange.to)) -}}
{{- end -}}
{{- $provider := .Values.database.provider -}}
{{- if not (has $provider (list "sqlite" "postgresql" "sqlserver")) -}}
{{- fail (printf "database.provider must be sqlite, postgresql or sqlserver, not %q." $provider) -}}
{{- end -}}
{{- if and (ne $provider "sqlite") (not .Values.database.existingSecret) -}}
{{- fail (printf "database.provider %s needs database.existingSecret, the Secret that holds the connection string." $provider) -}}
{{- end -}}
{{- if .Values.cluster.enabled -}}
{{- if eq $provider "sqlite" -}}
{{- fail "Cluster mode needs database.provider postgresql or sqlserver. SQLite is for single-node mode only." -}}
{{- end -}}
{{- $shared := .Values.persistence.shared -}}
{{- if not (or $shared.volume $shared.existingClaim) -}}
{{- fail "Cluster mode needs a shared volume. Set persistence.shared.volume or persistence.shared.existingClaim." -}}
{{- end -}}
{{- if .Values.persistence.data.existingClaim -}}
{{- fail "persistence.data.existingClaim is for single-node mode only, because each cluster node needs its own data volume." -}}
{{- end -}}
{{- else if gt (int .Values.replicaCount) 1 -}}
{{- fail "Single-node mode runs 1 replica. Set cluster.enabled to true to run more replicas." -}}
{{- end -}}
{{- if and .Values.keys.existingSecret (not .Values.secretKey.existingSecret) -}}
{{- fail "keys.existingSecret needs secretKey.existingSecret, because the mounted keys folder is read-only and Magpie cannot create its integration key there." -}}
{{- end -}}
{{- if and .Values.ingress.enabled (not .Values.ingress.hosts) -}}
{{- fail "ingress.enabled needs at least 1 host in ingress.hosts." -}}
{{- end -}}
{{- end -}}
