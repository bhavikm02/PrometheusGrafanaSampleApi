# Deploying .NET API with APM Tools on Amazon EKS

This guide provides comprehensive instructions for deploying the **PrometheusGrafanaSampleApi** (.NET 8.0) to Amazon EKS (Elastic Kubernetes Service) with a full observability stack including Prometheus, Grafana, Loki, Promtail, and Tempo.

## Project Overview

This project is already configured with:
- **Metrics**: Prometheus metrics endpoint at `/metrics` (using `prometheus-net.AspNetCore`)
- **Tracing**: OpenTelemetry traces exported to Tempo via OTLP (gRPC on port 4317)
- **Logging**: Structured JSON logging to stdout (collected by Promtail → Loki)

Current Docker setup uses:
- Port: `8080` (configurable via `PORT` env var)
- Service Name: `PrometheusGrafanaSampleApi` (configurable via `OTEL_SERVICE_NAME` env var)
- OTLP Endpoint: `http://tempo:4317` (default, configurable via `OTEL_EXPORTER_OTLP_ENDPOINT`)

## Table of Contents

- [Prerequisites](#prerequisites)
- [Architecture Overview](#architecture-overview)
- [Step 1: EKS Cluster Setup](#step-1-eks-cluster-setup)
- [Step 2: Container Registry Setup](#step-2-container-registry-setup)
- [Step 3: Deploy Observability Stack](#step-3-deploy-observability-stack)
  - [3.1 Deploy Prometheus](#31-deploy-prometheus)
  - [3.2 Deploy Loki](#32-deploy-loki)
  - [3.3 Deploy Promtail](#33-deploy-promtail)
  - [3.4 Deploy Tempo](#34-deploy-tempo)
  - [3.5 Deploy Grafana](#35-deploy-grafana)
- [Step 4: Deploy .NET API Application](#step-4-deploy-net-api-application)
- [Step 5: Configure Service Discovery](#step-5-configure-service-discovery)
- [Step 6: Access and Verification](#step-6-access-and-verification)
- [Step 7: Production Considerations](#step-7-production-considerations)
- [Troubleshooting](#troubleshooting)

---

## Prerequisites

Before starting, ensure you have the following tools installed:

- **AWS CLI** (v2.x or later)
- **kubectl** (compatible with your Kubernetes version)
- **eksctl** (v0.150.0 or later)
- **Helm** (v3.x or later)
- **Docker** (for building container images)
- **AWS Account** with appropriate IAM permissions

Configure AWS credentials:
```bash
aws configure
```

### Quick Migration from Docker Compose

If you're currently running this project with `docker-compose up`, here's what changes when moving to Kubernetes:

| Aspect | Docker Compose | Kubernetes/EKS |
|--------|----------------|-----------------|
| **Service Discovery** | Service names (`tempo:4317`) | DNS (`tempo.observability.svc.cluster.local:4317`) |
| **Log Collection** | Docker logs from host | Promtail DaemonSet per node |
| **Metrics Scraping** | Static config in `prometheus.yml` | Pod annotations + service discovery |
| **Scaling** | Manual via `docker-compose scale` | HorizontalPodAutoscaler |
| **Config** | Environment variables in `docker-compose.yml` | ConfigMaps/Secrets |
| **Storage** | Docker volumes | PersistentVolumes (EBS) |

**Key Differences:**
- Environment variables in Kubernetes use Kubernetes service DNS names
- Prometheus automatically discovers pods via annotations (no static config needed)
- Logs are collected from all pods across all nodes via Promtail DaemonSet
- All components get persistent storage via EBS volumes

## Architecture Overview

The deployment architecture consists of:

```
┌─────────────────────────────────────────────────────────────┐
│                         EKS Cluster                          │
│                                                               │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐  │
│  │              │    │              │    │              │  │
│  │  .NET API    │───▶│  Prometheus  │───▶│   Grafana    │  │
│  │  (Metrics)   │    │  (Metrics)   │    │ (Dashboards) │  │
│  │              │    │              │    │              │  │
│  └──────────────┘    └──────────────┘    └──────┬───────┘  │
│         │                                         │          │
│         │ OTLP                                    │          │
│         │                                         │          │
│         ▼                                         │          │
│  ┌──────────────┐                                 │          │
│  │    Tempo     │─────────────────────────────────┘          │
│  │  (Traces)    │                                            │
│  └──────────────┘                                            │
│         ▲                                                     │
│         │                                                     │
│  ┌──────────────┐    ┌──────────────┐                       │
│  │  Promtail    │───▶│     Loki     │───────────────────────┘
│  │ (Log Agent)  │    │    (Logs)    │                        
│  └──────────────┘    └──────────────┘                        
│                                                               │
└─────────────────────────────────────────────────────────────┘
```

---

## Step 1: EKS Cluster Setup

### 1.0 Organize Kubernetes Manifests (Optional)

Before starting, you may want to create a directory structure for your Kubernetes manifests:

```bash
mkdir -p k8s/{observability,app}
```

This will help organize your files:
- `k8s/observability/` - For Prometheus, Loki, Grafana, Tempo configs
- `k8s/app/` - For application deployment files

### 1.1 Create EKS Cluster

Create a cluster configuration file `eks-cluster.yaml`:

```yaml
apiVersion: eksctl.io/v1alpha5
kind: ClusterConfig

metadata:
  name: dotnet-apm-cluster
  region: us-east-1
  version: "1.28"

managedNodeGroups:
  - name: ng-1
    instanceType: t3.medium
    desiredCapacity: 3
    minSize: 2
    maxSize: 4
    volumeSize: 30
    ssh:
      allow: false
    labels:
      role: worker
    tags:
      environment: production
      project: dotnet-apm

cloudWatch:
  clusterLogging:
    enableTypes: ["api", "audit", "authenticator", "controllerManager", "scheduler"]

iam:
  withOIDC: true
```

Create the cluster:

```bash
eksctl create cluster -f eks-cluster.yaml
```

This process takes approximately 15-20 minutes.

### 1.2 Verify Cluster Access

```bash
kubectl get nodes
kubectl cluster-info
```

### 1.3 Create Namespaces

```bash
kubectl create namespace observability
kubectl create namespace app
```

---

## Step 2: Container Registry Setup

### 2.1 Create ECR Repository

```bash
aws ecr create-repository \
    --repository-name dotnet-api \
    --region us-east-1 \
    --image-scanning-configuration scanOnPush=true
```

### 2.2 Authenticate Docker to ECR

```bash
aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin <AWS_ACCOUNT_ID>.dkr.ecr.us-east-1.amazonaws.com
```

### 2.3 Build and Push Docker Image

```bash
# Get AWS account ID (if not already known)
AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
ECR_REGISTRY="${AWS_ACCOUNT_ID}.dkr.ecr.us-east-1.amazonaws.com"

# Build the image (same Dockerfile as used in docker-compose)
docker build -t dotnet-api:latest .

# Tag the image
docker tag dotnet-api:latest ${ECR_REGISTRY}/dotnet-api:latest

# Push to ECR
docker push ${ECR_REGISTRY}/dotnet-api:latest

# Note: Replace <AWS_ACCOUNT_ID> in all YAML files with ${AWS_ACCOUNT_ID} or your actual account ID
```

---

## Step 3: Deploy Observability Stack

### 3.1 Deploy Prometheus

#### Install using Helm (Recommended)

Add the Prometheus community Helm repository:

```bash
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm repo update
```

Create a values file `prometheus-values.yaml`:

```yaml
server:
  persistentVolume:
    enabled: true
    size: 20Gi
    storageClass: gp2
  
  retention: "15d"
  
  global:
    scrape_interval: 15s
    evaluation_interval: 15s
  
  service:
    type: ClusterIP
    port: 9090

alertmanager:
  enabled: false

nodeExporter:
  enabled: true

pushgateway:
  enabled: false

serverFiles:
  prometheus.yml:
    scrape_configs:
      - job_name: 'kubernetes-pods'
        kubernetes_sd_configs:
          - role: pod
            namespaces:
              names:
                - app
        relabel_configs:
          - source_labels: [__meta_kubernetes_pod_annotation_prometheus_io_scrape]
            action: keep
            regex: true
          - source_labels: [__meta_kubernetes_pod_annotation_prometheus_io_path]
            action: replace
            target_label: __metrics_path__
            regex: (.+)
          - source_labels: [__address__, __meta_kubernetes_pod_annotation_prometheus_io_port]
            action: replace
            regex: ([^:]+)(?::\d+)?;(\d+)
            replacement: $1:$2
            target_label: __address__
          - action: labelmap
            regex: __meta_kubernetes_pod_label_(.+)
          - source_labels: [__meta_kubernetes_namespace]
            action: replace
            target_label: kubernetes_namespace
          - source_labels: [__meta_kubernetes_pod_name]
            action: replace
            target_label: kubernetes_pod_name
```

Deploy Prometheus:

```bash
helm install prometheus prometheus-community/prometheus \
  --namespace observability \
  --create-namespace \
  --values prometheus-values.yaml
```

**Note:** The Prometheus configuration uses Kubernetes pod discovery which automatically finds pods with the `prometheus.io/scrape: "true"` annotation. This replaces the static `scrape_configs` from your `prometheus/prometheus.yml` Docker Compose configuration.

### 3.2 Deploy Loki

Add the Grafana Helm repository:

```bash
helm repo add grafana https://grafana.github.io/helm-charts
helm repo update
```

Create `loki-values.yaml`:

```yaml
loki:
  auth_enabled: false
  
  commonConfig:
    replication_factor: 1
  
  storage:
    type: filesystem
  
  schemaConfig:
    configs:
      - from: 2024-01-01
        store: tsdb
        object_store: filesystem
        schema: v12
        index:
          prefix: index_
          period: 24h

singleBinary:
  replicas: 1
  persistence:
    enabled: true
    size: 10Gi
    storageClass: gp2

monitoring:
  selfMonitoring:
    enabled: false
    grafanaAgent:
      installOperator: false

test:
  enabled: false

gateway:
  enabled: true
  service:
    type: ClusterIP
    port: 3100
```

Deploy Loki:

```bash
helm install loki grafana/loki \
  --namespace observability \
  --create-namespace \
  --values loki-values.yaml
```

**Note:** In Kubernetes, Loki runs as a Deployment (instead of a single container). The gateway service provides a stable endpoint for Promtail to push logs to, similar to `http://loki:3100` in Docker Compose.

### 3.3 Deploy Promtail

Create `promtail-values.yaml`:

```yaml
config:
  clients:
    - url: http://loki-gateway.observability.svc.cluster.local/loki/api/v1/push
  
  snippets:
    scrapeConfigs: |
      - job_name: kubernetes-pods
        pipeline_stages:
          - cri: {}
          - json:
              expressions:
                timestamp: time
                level: level
                message: message
          - labels:
              level:
        kubernetes_sd_configs:
          - role: pod
        relabel_configs:
          - source_labels: [__meta_kubernetes_pod_controller_name]
            regex: ([0-9a-z-.]+?)(-[0-9a-f]{8,10})?
            action: replace
            target_label: __tmp_controller_name
          - source_labels: [__meta_kubernetes_pod_label_app_kubernetes_io_name]
            action: replace
            target_label: app
          - source_labels: [__meta_kubernetes_namespace]
            action: replace
            target_label: namespace
          - source_labels: [__meta_kubernetes_pod_name]
            action: replace
            target_label: pod
          - source_labels: [__meta_kubernetes_pod_container_name]
            action: replace
            target_label: container
          - replacement: /var/log/pods/*$1/*.log
            separator: /
            source_labels:
              - __meta_kubernetes_pod_uid
              - __meta_kubernetes_pod_container_name
            target_label: __path__

daemonset:
  enabled: true

serviceMonitor:
  enabled: false
```

Deploy Promtail:

```bash
helm install promtail grafana/promtail \
  --namespace observability \
  --create-namespace \
  --values promtail-values.yaml
```

**Note:** Promtail runs as a DaemonSet (one pod per node) in Kubernetes, automatically collecting logs from all pods on each node. This replaces the Docker-specific log collection in your `promtail/config.yml` that reads from `/var/lib/docker/containers`.

### 3.4 Deploy Tempo

Create `tempo-config.yaml`:

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: tempo-config
  namespace: observability
data:
  tempo.yaml: |
    server:
      http_listen_port: 3200
    
    distributor:
      receivers:
        otlp:
          protocols:
            grpc:
              endpoint: 0.0.0.0:4317
            http:
              endpoint: 0.0.0.0:4318
    
    ingester:
      lifecycler:
        address: 0.0.0.0
        ring:
          kvstore:
            store: inmemory
          replication_factor: 1
      trace_idle_period: 30s
      max_block_bytes: 1048576
      max_block_duration: 10m
    
    compactor:
      compaction:
        compaction_window: 1h
        max_compaction_objects: 1000000
        block_retention: 48h
        compacted_block_retention: 1h
    
    storage:
      trace:
        backend: local
        local:
          path: /var/tempo/traces
        wal:
          path: /var/tempo/wal
    
    querier:
      frontend_worker:
        frontend_address: 127.0.0.1:9095
    
    query_frontend:
      search:
        max_duration: 0s
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: tempo
  namespace: observability
spec:
  replicas: 1
  selector:
    matchLabels:
      app: tempo
  template:
    metadata:
      labels:
        app: tempo
    spec:
      containers:
      - name: tempo
        image: grafana/tempo:latest
        args:
          - "-config.file=/etc/tempo/tempo.yaml"
        ports:
        - containerPort: 3200
          name: http
          protocol: TCP
        - containerPort: 4317
          name: otlp-grpc
          protocol: TCP
        - containerPort: 4318
          name: otlp-http
          protocol: TCP
        volumeMounts:
        - name: config
          mountPath: /etc/tempo
        - name: storage
          mountPath: /var/tempo
        resources:
          requests:
            memory: "256Mi"
            cpu: "100m"
          limits:
            memory: "512Mi"
            cpu: "500m"
      volumes:
      - name: config
        configMap:
          name: tempo-config
      - name: storage
        emptyDir: {}
---
apiVersion: v1
kind: Service
metadata:
  name: tempo
  namespace: observability
spec:
  type: ClusterIP
  ports:
  - port: 3200
    targetPort: 3200
    protocol: TCP
    name: http
  - port: 4317
    targetPort: 4317
    protocol: TCP
    name: otlp-grpc
  - port: 4318
    targetPort: 4318
    protocol: TCP
    name: otlp-http
  selector:
    app: tempo
```

Deploy Tempo:

```bash
kubectl apply -f tempo-config.yaml
```

### 3.5 Deploy Grafana

Create `grafana-values.yaml`:

```yaml
adminUser: admin
adminPassword: admin

persistence:
  enabled: true
  size: 10Gi
  storageClassName: gp2

service:
  type: LoadBalancer
  port: 3000

datasources:
  datasources.yaml:
    apiVersion: 1
    datasources:
    - name: Prometheus
      type: prometheus
      access: proxy
      url: http://prometheus-server.observability.svc.cluster.local
      isDefault: true
      editable: true
    
    - name: Loki
      type: loki
      access: proxy
      url: http://loki-gateway.observability.svc.cluster.local
      editable: true
    
    - name: Tempo
      type: tempo
      access: proxy
      url: http://tempo.observability.svc.cluster.local:3200
      editable: true
      jsonData:
        tracesToLogs:
          datasourceUid: 'loki'
          tags: ['pod', 'namespace']
        tracesToMetrics:
          datasourceUid: 'prometheus'
        serviceMap:
          datasourceUid: 'prometheus'
        nodeGraph:
          enabled: true

dashboardProviders:
  dashboardproviders.yaml:
    apiVersion: 1
    providers:
    - name: 'default'
      orgId: 1
      folder: ''
      type: file
      disableDeletion: false
      editable: true
      options:
        path: /var/lib/grafana/dashboards/default

dashboards:
  default:
    kubernetes-cluster:
      gnetId: 7249
      revision: 1
      datasource: Prometheus
    
    kubernetes-pods:
      gnetId: 6417
      revision: 1
      datasource: Prometheus

env:
  GF_FEATURE_TOGGLES_ENABLE: "traceToMetrics"

resources:
  requests:
    memory: "256Mi"
    cpu: "100m"
  limits:
    memory: "512Mi"
    cpu: "500m"
```

Deploy Grafana:

```bash
helm install grafana grafana/grafana \
  --namespace observability \
  --values grafana-values.yaml
```

---

## Step 4: Deploy .NET API Application

### 4.1 Create Application ConfigMap

Create `app-config.yaml`:

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: dotnet-api-config
  namespace: app
data:
  ASPNETCORE_ENVIRONMENT: "Production"
  PORT: "8080"
  OTEL_SERVICE_NAME: "PrometheusGrafanaSampleApi"
  OTEL_EXPORTER_OTLP_ENDPOINT: "http://tempo.observability.svc.cluster.local:4317"
```

Apply the ConfigMap:

```bash
kubectl apply -f app-config.yaml
```

### 4.2 Create Application Deployment

Create `app-deployment.yaml`:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: dotnet-api
  namespace: app
  labels:
    app: dotnet-api
spec:
  replicas: 3
  selector:
    matchLabels:
      app: dotnet-api
  template:
    metadata:
      labels:
        app: dotnet-api
      annotations:
        prometheus.io/scrape: "true"
        prometheus.io/port: "8080"
        prometheus.io/path: "/metrics"
    spec:
      containers:
      - name: api
        image: <AWS_ACCOUNT_ID>.dkr.ecr.us-east-1.amazonaws.com/dotnet-api:latest
        imagePullPolicy: Always
        ports:
        - containerPort: 8080
          name: http
          protocol: TCP
        envFrom:
        - configMapRef:
            name: dotnet-api-config
        livenessProbe:
          httpGet:
            path: /swagger/index.html
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 10
          timeoutSeconds: 5
          failureThreshold: 3
        readinessProbe:
          httpGet:
            path: /swagger/index.html
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 5
          timeoutSeconds: 3
          failureThreshold: 3
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
---
apiVersion: v1
kind: Service
metadata:
  name: dotnet-api-service
  namespace: app
  labels:
    app: dotnet-api
spec:
  type: LoadBalancer
  ports:
  - port: 80
    targetPort: 8080
    protocol: TCP
    name: http
  selector:
    app: dotnet-api
---
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: dotnet-api-hpa
  namespace: app
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: dotnet-api
  minReplicas: 3
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
```

Deploy the application:

```bash
kubectl apply -f app-deployment.yaml
```

### 4.3 Verify Deployment

```bash
# Check pod status
kubectl get pods -n app
kubectl get svc -n app

# View logs
kubectl logs -n app -l app=dotnet-api --tail=50

# Check if metrics endpoint is accessible
kubectl port-forward -n app svc/dotnet-api-service 8080:80
# Then test: curl http://localhost:8080/metrics

# Verify OTLP connectivity from a pod
kubectl exec -n app deployment/dotnet-api -- curl -v http://tempo.observability.svc.cluster.local:4317
```

---

## Step 5: Configure Service Discovery

### 5.1 Create ServiceMonitor (Optional - if using Prometheus Operator)

```yaml
apiVersion: monitoring.coreos.com/v1
kind: ServiceMonitor
metadata:
  name: dotnet-api-monitor
  namespace: app
  labels:
    app: dotnet-api
spec:
  selector:
    matchLabels:
      app: dotnet-api
  endpoints:
  - port: http
    path: /metrics
    interval: 15s
```

### 5.2 Verify Service Discovery

Check that Prometheus is discovering your application:

```bash
# Port forward to Prometheus
kubectl port-forward -n observability svc/prometheus-server 9090:9090

# Visit http://localhost:9090/targets to see discovered targets
```

---

## Step 6: Access and Verification

### 6.1 Get Service Endpoints

```bash
# Get Grafana LoadBalancer URL
kubectl get svc -n observability grafana -o jsonpath='{.status.loadBalancer.ingress[0].hostname}'

# Get API LoadBalancer URL
kubectl get svc -n app dotnet-api-service -o jsonpath='{.status.loadBalancer.ingress[0].hostname}'
```

### 6.2 Access Grafana

1. Navigate to the Grafana LoadBalancer URL
2. Login with credentials (default: admin/admin)
3. Verify data sources are connected:
   - Go to **Configuration** → **Data Sources**
   - Check Prometheus, Loki, and Tempo connections

### 6.3 Import Custom Dashboard

The project includes a dashboard at `grafana/provisioning/dashboards/json/api-overview.json`. To use it in Kubernetes:

#### Option 1: Via Helm Values (Recommended)

Update `grafana-values.yaml` to include:

```yaml
dashboards:
  default:
    api-overview:
      gnetId: null
      revision: null
      datasource: Prometheus
      url: https://raw.githubusercontent.com/YOUR_REPO/main/grafana/provisioning/dashboards/json/api-overview.json
```

Or mount as ConfigMap:

```bash
# Create ConfigMap from existing dashboard
kubectl create configmap api-dashboard \
  --from-file=api-overview.json=./grafana/provisioning/dashboards/json/api-overview.json \
  --namespace observability

# Update grafana-values.yaml to mount the ConfigMap:
# dashboards:
#   default:
#     api-overview:
#       configMapName: api-dashboard
#       configMapKey: api-overview.json
```

#### Option 2: Manual Import via Grafana UI

1. Log into Grafana
2. Go to **Dashboards** → **Import**
3. Upload `grafana/provisioning/dashboards/json/api-overview.json`

### 6.4 Test the Application

```bash
# Get the API endpoint
API_URL=$(kubectl get svc -n app dotnet-api-service -o jsonpath='{.status.loadBalancer.ingress[0].hostname}')

# Test the API
curl http://$API_URL/swagger/index.html

# Generate some traffic for metrics
for i in {1..100}; do
  curl -X GET "http://$API_URL/api/todos" -H "accept: application/json"
  sleep 1
done
```

### 6.5 Verify Observability Data

#### Metrics (Prometheus)
```bash
kubectl port-forward -n observability svc/prometheus-server 9090:9090
# Visit http://localhost:9090

# Test queries:
# - HTTP request rate: rate(http_requests_received_total[5m])
# - Request duration: histogram_quantile(0.95, rate(http_request_duration_seconds_bucket[5m]))
# - Active requests: sum(http_requests_received_total) by (pod)
```

#### Logs (Loki)
In Grafana:
- Go to **Explore**
- Select **Loki** data source
- Query examples:
  - All logs: `{namespace="app", app="dotnet-api"}`
  - Error logs: `{namespace="app", app="dotnet-api"} |= "error"`
  - Logs by pod: `{namespace="app", pod=~"dotnet-api-.*"}`

#### Traces (Tempo)
In Grafana:
- Go to **Explore**
- Select **Tempo** data source
- Search for traces or use "Search" tab

---

## Step 7: Production Considerations

### 7.1 Persistent Storage

For production, use EBS volumes with proper storage classes:

```yaml
apiVersion: storage.k8s.io/v1
kind: StorageClass
metadata:
  name: gp3-encrypted
provisioner: ebs.csi.aws.com
parameters:
  type: gp3
  encrypted: "true"
  iops: "3000"
  throughput: "125"
volumeBindingMode: WaitForFirstConsumer
allowVolumeExpansion: true
```

### 7.2 Security Enhancements

#### Enable IRSA (IAM Roles for Service Accounts)

```bash
# Create IAM policy for EBS CSI driver
eksctl create iamserviceaccount \
  --name ebs-csi-controller-sa \
  --namespace kube-system \
  --cluster dotnet-apm-cluster \
  --attach-policy-arn arn:aws:iam::aws:policy/service-role/AmazonEBSCSIDriverPolicy \
  --approve \
  --role-only \
  --role-name AmazonEKS_EBS_CSI_DriverRole
```

#### Network Policies

Create `network-policy.yaml`:

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: allow-app-to-observability
  namespace: app
spec:
  podSelector:
    matchLabels:
      app: dotnet-api
  policyTypes:
  - Egress
  egress:
  - to:
    - namespaceSelector:
        matchLabels:
          name: observability
    ports:
    - protocol: TCP
      port: 4317  # Tempo gRPC
    - protocol: TCP
      port: 4318  # Tempo HTTP
  - to:
    - podSelector: {}
  - ports:
    - port: 53
      protocol: UDP
    - port: 53
      protocol: TCP
```

### 7.3 Resource Quotas

Create `resource-quota.yaml`:

```yaml
apiVersion: v1
kind: ResourceQuota
metadata:
  name: app-quota
  namespace: app
spec:
  hard:
    requests.cpu: "4"
    requests.memory: 8Gi
    limits.cpu: "8"
    limits.memory: 16Gi
    persistentvolumeclaims: "10"
```

### 7.4 Monitoring and Alerting

Create Prometheus alerting rules `prometheus-alerts.yaml`:

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: prometheus-alerts
  namespace: observability
data:
  alerts.yml: |
    groups:
    - name: dotnet-api
      interval: 30s
      rules:
      - alert: HighErrorRate
        expr: |
          rate(http_requests_received_total{code=~"5.."}[5m]) > 0.05
        for: 5m
        labels:
          severity: critical
        annotations:
          summary: "High error rate detected"
          description: "Error rate is {{ $value }} for {{ $labels.pod }}"
      
      - alert: HighMemoryUsage
        expr: |
          container_memory_usage_bytes{namespace="app",pod=~"dotnet-api.*"} 
          / container_spec_memory_limit_bytes{namespace="app",pod=~"dotnet-api.*"} > 0.9
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "High memory usage"
          description: "Memory usage is {{ $value | humanizePercentage }} for {{ $labels.pod }}"
      
      - alert: PodCrashLooping
        expr: |
          rate(kube_pod_container_status_restarts_total{namespace="app"}[15m]) > 0
        for: 5m
        labels:
          severity: critical
        annotations:
          summary: "Pod is crash looping"
          description: "Pod {{ $labels.pod }} is restarting frequently"
```

### 7.5 High Availability

Update Prometheus and Grafana for HA:

```bash
# Update Prometheus to use 2 replicas
helm upgrade prometheus prometheus-community/prometheus \
  --namespace observability \
  --set server.replicaCount=2 \
  --reuse-values

# Update Grafana to use 2 replicas
helm upgrade grafana grafana/grafana \
  --namespace observability \
  --set replicas=2 \
  --reuse-values
```

### 7.6 Backup and Disaster Recovery

Create a backup script for Grafana dashboards:

```bash
#!/bin/bash
# backup-grafana.sh

GRAFANA_URL="http://admin:admin@localhost:3000"
BACKUP_DIR="./grafana-backups/$(date +%Y%m%d)"

mkdir -p "$BACKUP_DIR"

# Port forward to Grafana
kubectl port-forward -n observability svc/grafana 3000:3000 &
PF_PID=$!
sleep 5

# Backup all dashboards
curl -s "$GRAFANA_URL/api/search?query=&" | \
  jq -r '.[] | select(.type == "dash-db") | .uid' | \
  while read uid; do
    curl -s "$GRAFANA_URL/api/dashboards/uid/$uid" | \
      jq . > "$BACKUP_DIR/$uid.json"
  done

kill $PF_PID
echo "Backup completed in $BACKUP_DIR"
```

### 7.7 Cost Optimization

- Use **Spot Instances** for non-critical workloads
- Implement **Cluster Autoscaler** for dynamic scaling
- Use **Fargate** for specific workloads to reduce overhead
- Enable **EBS volume snapshots** for data persistence

```bash
# Install Cluster Autoscaler
kubectl apply -f https://raw.githubusercontent.com/kubernetes/autoscaler/master/cluster-autoscaler/cloudprovider/aws/examples/cluster-autoscaler-autodiscover.yaml
```

---

## Troubleshooting

### Common Issues

#### 1. Pods Not Starting

```bash
# Check pod status
kubectl get pods -n app
kubectl describe pod <pod-name> -n app
kubectl logs <pod-name> -n app
```

#### 2. Metrics Not Appearing in Prometheus

```bash
# Verify pod annotations
kubectl get pod -n app -l app=dotnet-api -o jsonpath='{.items[0].metadata.annotations}'

# Check Prometheus targets
kubectl port-forward -n observability svc/prometheus-server 9090:9090
# Visit http://localhost:9090/targets
```

#### 3. Traces Not Appearing in Tempo

```bash
# Check Tempo logs
kubectl logs -n observability deployment/tempo

# Verify OTLP endpoint connectivity from app
kubectl exec -n app <pod-name> -- curl -v http://tempo.observability.svc.cluster.local:4318
```

#### 4. Logs Not Appearing in Loki

```bash
# Check Promtail logs
kubectl logs -n observability daemonset/promtail

# Verify Loki is receiving logs
kubectl logs -n observability deployment/loki-0
```

#### 5. LoadBalancer Stuck in Pending

```bash
# Check events
kubectl describe svc -n app dotnet-api-service

# Verify AWS Load Balancer Controller is installed
kubectl get deployment -n kube-system aws-load-balancer-controller
```

If not installed:
```bash
# Install AWS Load Balancer Controller
helm repo add eks https://aws.github.io/eks-charts
helm install aws-load-balancer-controller eks/aws-load-balancer-controller \
  --namespace kube-system \
  --set clusterName=dotnet-apm-cluster
```

### Debugging Commands

```bash
# View all resources in a namespace
kubectl get all -n app
kubectl get all -n observability

# Check resource usage
kubectl top nodes
kubectl top pods -n app

# View events
kubectl get events -n app --sort-by='.lastTimestamp'

# Execute commands in a pod
kubectl exec -it -n app <pod-name> -- /bin/bash

# View detailed pod information
kubectl get pod <pod-name> -n app -o yaml
```

---

## Summary

This guide covered the complete deployment of **PrometheusGrafanaSampleApi** (.NET 8.0) with a comprehensive APM stack on Amazon EKS:

1. ✅ Created and configured an EKS cluster
2. ✅ Set up ECR for container image management
3. ✅ Deployed Prometheus for metrics collection (scrapes `/metrics` endpoint)
4. ✅ Deployed Loki and Promtail for centralized logging (collects stdout logs)
5. ✅ Deployed Tempo for distributed tracing (receives OTLP from OpenTelemetry SDK)
6. ✅ Deployed Grafana for unified visualization
7. ✅ Deployed the .NET API application with existing instrumentation:
   - Prometheus metrics via `prometheus-net.AspNetCore`
   - OpenTelemetry tracing via OTLP exporter
   - Structured JSON logging to stdout
8. ✅ Configured Kubernetes service discovery for automatic scraping
9. ✅ Implemented production-ready features (HA, security, autoscaling)

### Project-Specific Configuration Summary

| Component | Docker Compose | Kubernetes | Notes |
|-----------|---------------|------------|-------|
| API Port | 8080 | 8080 | Configurable via `PORT` env var |
| Metrics Endpoint | `/metrics` | `/metrics` | Auto-exposed by `app.MapMetrics()` |
| Service Name | `PrometheusGrafanaSampleApi` | `PrometheusGrafanaSampleApi` | Configurable via `OTEL_SERVICE_NAME` |
| OTLP Endpoint | `http://tempo:4317` | `http://tempo.observability.svc.cluster.local:4317` | Configurable via `OTEL_EXPORTER_OTLP_ENDPOINT` |
| Logging | stdout (JSON) | stdout (JSON) | Collected by Promtail DaemonSet |

### Next Steps

- Configure **AlertManager** for alert notifications (Slack, PagerDuty, email)
- Implement **GitOps** with ArgoCD or Flux for declarative deployments
- Set up **CI/CD pipelines** with AWS CodePipeline or GitHub Actions
- Configure **Ingress Controller** (NGINX or AWS ALB) for better routing
- Implement **Service Mesh** (Istio or Linkerd) for advanced traffic management
- Enable **Pod Security Policies** or **Pod Security Standards**
- Configure **Secrets Management** with AWS Secrets Manager or HashiCorp Vault

### Useful Resources

- [EKS Best Practices Guide](https://aws.github.io/aws-eks-best-practices/)
- [Prometheus Operator Documentation](https://prometheus-operator.dev/)
- [Grafana Loki Documentation](https://grafana.com/docs/loki/latest/)
- [Grafana Tempo Documentation](https://grafana.com/docs/tempo/latest/)
- [OpenTelemetry .NET Documentation](https://opentelemetry.io/docs/instrumentation/net/)
- [Kubernetes Documentation](https://kubernetes.io/docs/home/)

---

**Document Version:** 1.0  
**Last Updated:** November 2, 2025  
**Author:** Generated for PrometheusGrafanaSampleApi EKS Deployment

## YAML-by-YAML explanations (what • why • how) with mini diagrams

This section summarizes each YAML manifest used in this guide. For every file you’ll see a quick diagram plus: what it defines, why you need it, and how it works or how to customize it.

### eks-cluster.yaml — EKS cluster definition

Diagram

```
AWS Account
  └─ EKS Cluster (dotnet-apm-cluster, v1.28)
      └─ Managed NodeGroup (t3.medium x 3 desired)
         ├─ OIDC enabled for IRSA
         └─ Control plane logging → CloudWatch
```

- What: Declarative cluster config for eksctl that creates the EKS control plane and a managed worker node group.
- Why: Reproducible, versioned cluster creation with best-practice defaults (OIDC for IRSA, logging enabled).
- How: `eksctl create cluster -f eks-cluster.yaml`. Tweak region, Kubernetes version, instance types, capacity and logging as needed.

Key fields
- `metadata.version`: Kubernetes version to provision
- `managedNodeGroups[*].instanceType/desiredCapacity`: worker sizing
- `iam.withOIDC`: enables service account roles (IRSA)
- `cloudWatch.clusterLogging.enableTypes`: control plane logs sent to CloudWatch

---

### prometheus-values.yaml — Prometheus Helm chart overrides

Diagram

```
Prometheus Server (ClusterIP)
  └─ Scrape: kubernetes-pods
        └─ Filter: namespace=app
           └─ Uses pod annotations: prometheus.io/scrape, path, port
```

- What: Values for the `prometheus-community/prometheus` Helm chart to configure storage, retention, and pod scraping.
- Why: Prometheus needs to discover and scrape your app metrics endpoint automatically. Using pod annotations avoids per-service configs.
- How: `helm install prometheus ... --values prometheus-values.yaml`. Edit `server.persistentVolume.size`, `retention`, and scrape configs for your namespaces.

Key bits
- `serverFiles.prometheus.yml.scrape_configs`: configures discovery via `kubernetes_sd_configs` and relabeling to honor annotations.
- Persistence: enables PV for metrics retention across pod restarts.

Annotations needed on pods
- `prometheus.io/scrape: "true"`
- `prometheus.io/port: "8080"`
- `prometheus.io/path: "/metrics"`

---

### loki-values.yaml — Loki (logs database) overrides

Diagram

```
Loki (singleBinary, filesystem storage)
  └─ Gateway (ClusterIP:3100)
      └─ Receives pushes from Promtail
```

- What: Values for the `grafana/loki` Helm chart deploying a single-binary Loki with filesystem storage and a gateway service.
- Why: Central log store for Kubernetes pods, queryable from Grafana.
- How: `helm install loki ... --values loki-values.yaml`. For production, switch to object storage (S3) and increase replicas.

Key bits
- `singleBinary.persistence.enabled`: retains log indexes on a PV
- `gateway.enabled`: provides a stable in-cluster HTTP endpoint for Promtail clients

---

### promtail-values.yaml — Promtail (log shipper) DaemonSet

Diagram

```
Node1       Node2       Node3
  │           │           │
Promtail    Promtail    Promtail  (DaemonSet)
  │           │           │
  └──────────► Loki Gateway (HTTP push)
```

- What: Values for the `grafana/promtail` chart to tail container logs on every node and push them to Loki.
- Why: Stream application and system logs into a central, queryable store.
- How: `helm install promtail ... --values promtail-values.yaml`. The config includes Kubernetes service discovery and label enrichment.

Key bits
- `config.clients[0].url`: targets Loki gateway URL in the `observability` namespace
- `daemonset.enabled`: ensures one Promtail per node
- `snippets.scrapeConfigs`: CRI log parsing and relabeling to attach `namespace`, `pod`, `container` labels

---

### tempo-config.yaml — Tempo ConfigMap, Deployment, and Service

Diagram

```
App (.NET OpenTelemetry SDK)
  └─ OTLP gRPC/HTTP (4317/4318) → Tempo (Deployment)
                                 └─ Storage: emptyDir (dev) / PV (prod)
```

- What: Plain manifests to deploy Grafana Tempo for receiving and storing distributed traces.
- Why: Receives OTLP traces from your .NET API and makes them queryable in Grafana.
- How: `kubectl apply -f tempo-config.yaml`. For production, back storage by a PV or object store and scale out compactor/ingester components.

Key bits
- ConfigMap `tempo.yaml`: enables OTLP receivers and local storage paths
- Deployment ports: 4317 (gRPC), 4318 (HTTP), 3200 (UI/API)
- Service: ClusterIP exposing the three ports internally

App config
- Set `OTEL_EXPORTER_OTLP_ENDPOINT` to `http://tempo.observability.svc.cluster.local:4317` (gRPC) as used by your Program.cs

---

### grafana-values.yaml — Grafana Helm chart overrides

Diagram

```
Grafana (LoadBalancer:3000)
  ├─ DataSources
  │   ├─ Prometheus → http://prometheus-server
  │   ├─ Loki       → http://loki-gateway
  │   └─ Tempo      → http://tempo:3200
  └─ Dashboards (imported by IDs or files)
```

- What: Values for the `grafana/grafana` chart to expose Grafana via LoadBalancer, pre-provision data sources, and import dashboards.
- Why: One place to visualize metrics, logs, and traces with built-in correlation (traces-to-logs, traces-to-metrics).
- How: `helm install grafana ... --values grafana-values.yaml`. Change `adminUser/password`, LB type, and dashboard list to your preference.

Key bits
- `datasources.datasources.yaml.datasources`: wires Prometheus/Loki/Tempo
- `service.type: LoadBalancer`: get a public endpoint (or use Ingress)
- `dashboardProviders` + `dashboards`: pre-load community or custom dashboards

---

### app-config.yaml — Application ConfigMap (env vars)

Diagram

```
ConfigMap: dotnet-api-config
  ├─ ASPNETCORE_ENVIRONMENT=Production
  ├─ PORT=8080
  ├─ OTEL_SERVICE_NAME=PrometheusGrafanaSampleApi
  └─ OTEL_EXPORTER_OTLP_ENDPOINT=http://tempo:4317
```

- What: Centralizes environment variables for the .NET API pods.
- Why: Keep image immutable while changing environment per cluster/namespace.
- How: `kubectl apply -f app-config.yaml`. Mounted with `envFrom.configMapRef` in the Deployment.

Key bits
- Values match what your `Program.cs` reads to configure port and OTLP exporter.

---

### app-deployment.yaml — App Deployment, Service, and HPA

Diagram

```
Deployment (replicas=3) ──► Pods (annotated for Prometheus)
        │
        ├─ Service (LoadBalancer:80 → 8080)
        │
        └─ HPA (CPU 70%, Mem 80%)
```

- What: Runs the API across 3 replicas, exposes it publicly, and autoscale based on CPU/memory.
- Why: Achieve resiliency, horizontal scaling, and external access.
- How: `kubectl apply -f app-deployment.yaml`. Update image to your ECR URL; adjust probes/resources.

Key bits
- Deployment annotations `prometheus.io/*`: opt-in scraping on port 8080 at `/metrics`
- Liveness/Readiness probes: use `/swagger/index.html` to detect health
- HPA v2: scales between 3 and 10 replicas
- Service type LoadBalancer: allocates an AWS NLB/ALB endpoint (with the controller)

Observability paths
- Metrics: `UseHttpMetrics()` exposes `/metrics`
- Traces: OpenTelemetry SDK exports to Tempo
- Logs: Shipped by Promtail from container stdout/stderr

---

### ServiceMonitor (optional) — Prometheus Operator CRD

Diagram

```
Prometheus Operator
  └─ ServiceMonitor (namespace: app)
        └─ Selects Service port "http" → /metrics every 15s
```

- What: Custom resource used by Prometheus Operator to define scrape targets via Services instead of pod annotations.
- Why: Better governance and multi-team control over what gets scraped.
- How: Install Prometheus Operator, then `kubectl apply -f servicemonitor.yaml`. Ensure your Prometheus instance selects this namespace/label.

---

### gp3-encrypted StorageClass — Persistent volumes on EBS

Diagram

```
StorageClass (gp3-encrypted)
  └─ PVs for Prometheus/Loki/Grafana
        └─ Encrypted EBS volumes (gp3)
```

- What: Defines an encrypted EBS storage class with gp3 performance.
- Why: Durable, encrypted storage for stateful components in production.
- How: `kubectl apply -f storageclass.yaml`, then reference `storageClassName: gp3-encrypted` in Helm values/manifests.

---

### network-policy.yaml — Limit egress from app pods

Diagram

```
App namespace
  └─ NetworkPolicy: allow egress
      ├─ to observability namespace (4317/4318)
      └─ to DNS (TCP/UDP 53)
```

- What: Restricts app pods to only talk to observability components and DNS.
- Why: Principle of least privilege; reduce lateral movement and data exfiltration.
- How: `kubectl apply -f network-policy.yaml`. Make sure your CNI supports NetworkPolicy (AWS VPC CNI with Calico for policies).

---

### resource-quota.yaml — Cap namespace resource usage

Diagram

```
Namespace: app
  └─ ResourceQuota
      ├─ requests.cpu / memory
      ├─ limits.cpu / memory
      └─ pvc count
```

- What: Enforces upper bounds for total compute and storage claims in the namespace.
- Why: Prevents one team/workload from exhausting cluster capacity.
- How: `kubectl apply -f resource-quota.yaml`. Tune limits to your cluster size and SLOs.

---

### prometheus-alerts.yaml — Alerting rules (ConfigMap)

Diagram

```
Prometheus Rules
  ├─ HighErrorRate
  ├─ HighMemoryUsage
  └─ PodCrashLooping
```

- What: Collection of alerting rules grouped under a ConfigMap to be mounted by Prometheus.
- Why: Early detection of failures, performance regressions, and instability.
- How: `kubectl apply -f prometheus-alerts.yaml` then reference from Prometheus values under `serverFiles.alerts` or mount the CM. Wire Alertmanager for notifications (Slack, email, PagerDuty).

Rule intent
- HighErrorRate: 5xx responses exceed threshold over 5m
- HighMemoryUsage: container usage over 90% of limit for 10m
- PodCrashLooping: restarts > 0 over 15m

---

### Putting it all together (signal flow)

```
Users ─► AWS LB ─► Service (dotnet-api) ─► Pods (3)
                             │             ├─ /metrics scraped by Prometheus
                             │             ├─ stdout logs → Promtail → Loki
                             │             └─ OTLP traces → Tempo

Grafana (LB:3000)
  ├─ Prometheus (metrics)
  ├─ Loki (logs)
  └─ Tempo (traces)
```

Tips
- Start small (single-replica Loki/Tempo, local storage), then migrate to HA + durable storage.
- Keep app pods annotated for Prometheus even if you adopt ServiceMonitor, so local/dev remains easy.
- Standardize labels (`app`, `namespace`, `pod`) to simplify log/metric queries and dashboard templating.

