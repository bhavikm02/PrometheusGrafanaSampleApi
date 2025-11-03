# Grafana Dashboard Import Guide

## Auto-Provisioned Dashboard ✅

A custom **API Overview Dashboard** has been automatically provisioned and is ready to use!

### Access the Dashboard:
1. Open Grafana: http://localhost:3000
2. Login: `admin` / `admin`
3. Go to **Dashboards** → **Browse**
4. Look for **"API Overview Dashboard"**

### Dashboard Features:
- **HTTP Request Rate**: Real-time request throughput
- **95th Percentile Response Time**: Performance gauge
- **HTTP Response Status Codes**: Success/error rates (2xx, 4xx, 5xx)
- **Application Logs**: Live logs from Loki

---

## Recommended Community Dashboards to Import

### 1. ASP.NET Core Metrics (Recommended ⭐)
- **Dashboard ID**: `10915`
- **Best for**: .NET Core applications with prometheus-net
- **Metrics**: HTTP requests, response times, exceptions, GC stats

**How to Import:**
1. In Grafana, click **+** → **Import**
2. Enter ID: `10915`
3. Click **Load**
4. Select datasource: **Prometheus**
5. Click **Import**

---

### 2. Prometheus 2.0 Stats
- **Dashboard ID**: `2`
- **Best for**: Monitoring Prometheus health
- **Metrics**: Scrape duration, samples ingested, storage

**Import**: Same steps as above with ID `2`

---

### 3. Loki & Promtail Logs
- **Dashboard ID**: `13639`
- **Best for**: Application log analysis
- **Features**: Log volume, filtering, search

**Import Steps:**
1. Click **+** → **Import**
2. Enter ID: `13639`
3. Select datasource: **Loki**
4. Click **Import**

---

### 4. Docker Container Monitoring
- **Dashboard ID**: `193`
- **Best for**: Container resource usage
- **Metrics**: CPU, memory, network, disk I/O

**Note**: Requires cAdvisor (not currently in your setup)

---

## Manual Dashboard Import from JSON

### Option 1: From URL
1. Go to https://grafana.com/grafana/dashboards/
2. Find a dashboard you like
3. Copy the dashboard ID
4. In Grafana: **+** → **Import** → Paste ID

### Option 2: From File
1. Download dashboard JSON from grafana.com
2. Save to `grafana/provisioning/dashboards/json/`
3. Restart Grafana: `docker-compose restart grafana`

---

## Creating Custom Dashboards

### Using Prometheus Metrics
Your API exposes these metrics at `http://localhost:8080/metrics`:
- `http_requests_received_total` - Total HTTP requests
- `http_request_duration_seconds` - Request duration histogram
- `process_cpu_seconds_total` - CPU usage
- `dotnet_total_memory_bytes` - Memory usage

### Using Loki Logs
Query examples in Grafana Explore:
```logql
{job="docker"}                          # All container logs
{job="docker"} |= "error"              # Error logs only
{job="docker"} | json | level="Error"  # Structured error logs
```

---

## Useful Dashboard Queries

### Prometheus PromQL Examples:

**Request Rate:**
```promql
rate(http_requests_received_total[1m])
```

**Response Time (95th percentile):**
```promql
histogram_quantile(0.95, rate(http_request_duration_seconds_bucket[5m]))
```

**Error Rate:**
```promql
rate(http_requests_received_total{code=~"5.."}[1m])
```

**Memory Usage:**
```promql
process_working_set_bytes / 1024 / 1024
```

---

## Next Steps

1. ✅ Access the auto-provisioned dashboard
2. Import recommended community dashboards (IDs: 10915, 2, 13639)
3. Generate some API traffic to see metrics
4. Customize dashboards to your needs
5. Set up alerting rules (optional)

## Generate Test Traffic

```powershell
# Create some todos
Invoke-WebRequest -Uri "http://localhost:8080/api/todos" -Method Post -Body '{"title":"Test Todo","isComplete":false}' -ContentType "application/json"

# Get todos (generates metrics)
1..10 | ForEach-Object { Invoke-WebRequest -Uri "http://localhost:8080/api/todos" }
```

---

## Resources
- Grafana Dashboards: https://grafana.com/grafana/dashboards/
- Prometheus Queries: https://prometheus.io/docs/prometheus/latest/querying/basics/
- Loki LogQL: https://grafana.com/docs/loki/latest/logql/
