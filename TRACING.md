# Distributed Tracing with Grafana Tempo

Your application now supports **distributed tracing** using OpenTelemetry and Grafana Tempo!

## 🚀 What's Configured

### Tracing Stack:
- **OpenTelemetry** - Instrumentation library in your .NET API
- **Grafana Tempo** - Trace storage and query backend
- **Grafana** - UI for viewing and analyzing traces

### Instrumented Components:
- ✅ ASP.NET Core HTTP requests
- ✅ HttpClient calls
- ✅ Automatic trace context propagation

---

## 📊 How to View Traces in Grafana

**Important**: Tempo doesn't have a web UI at http://localhost:3200. You must view traces through **Grafana**.

### Step 1: Open Grafana
Navigate to: **http://localhost:3000** 
Login: `admin` / `admin`

### Step 2: Access Tempo
Two ways to view traces:

#### Option A: Explore View (Recommended)
1. Click **Explore** (compass icon) in the left sidebar
2. Select **Tempo** from the datasource dropdown (top-left)
3. Choose a query type:
   - **Search** - Find traces by service, duration, tags
   - **TraceQL** - Advanced query language for traces
4. Click **Run query**

#### Option B: From Logs (Traces-to-Logs Integration)
1. Go to **Explore** → Select **Loki** datasource
2. Run a log query: `{job="docker"}`
3. Click on a log line with trace information
4. Click the **Tempo** button to jump to the trace

---

## 🔍 Sample Queries

### Search Traces
In Grafana Explore with Tempo datasource:

**Find all traces from your API:**
```
Service Name: PrometheusGrafanaSampleApi
```

**Find slow requests (>100ms):**
```
Service Name: PrometheusGrafanaSampleApi
Min Duration: 100ms
```

### TraceQL Queries
Advanced trace query language:

**Find traces with errors:**
```traceql
{ status = error }
```

**Find GET requests:**
```traceql
{ span.http.method = "GET" }
```

**Find traces for a specific endpoint:**
```traceql
{ name =~ ".*todos.*" }
```

---

## 📈 What You Can See in a Trace

Each trace shows:
- **Request Flow** - Visual timeline of all operations
- **Span Duration** - How long each operation took
- **Tags/Attributes** - HTTP method, status code, route, etc.
- **Service Dependencies** - If you add more services, see how they interact
- **Errors** - Exceptions and stack traces (when `RecordException = true`)

### Example Trace Structure:
```
PrometheusGrafanaSampleApi
└─ GET /api/todos [200ms]
   ├─ Database Query [50ms]
   └─ Serialization [10ms]
```

---

## 🧪 Generate Test Traces

Run these commands to create sample traces:

### PowerShell:
```powershell
# Generate 10 requests
1..10 | ForEach-Object { 
    Invoke-WebRequest -Uri "http://localhost:8080/api/todos" 
    Start-Sleep -Milliseconds 200 
}

# Create a todo (POST request)
$body = @{ title = "Test Todo"; isComplete = $false } | ConvertTo-Json
Invoke-WebRequest -Uri "http://localhost:8080/api/todos" -Method Post -Body $body -ContentType "application/json"

# Get specific todo (will generate trace with ID parameter)
Invoke-WebRequest -Uri "http://localhost:8080/api/todos/1"
```

---

## 🔗 Traces Integration

### Traces → Logs
- Click on any span in a trace
- See **Logs for this span** link
- Automatically filters Loki logs for the same timeframe

### Traces → Metrics
- Future enhancement: Link traces to Prometheus metrics
- Use exemplars to jump from metrics to traces

---

## ⚙️ Configuration Details

### OpenTelemetry Exporter:
- **Protocol**: OTLP/gRPC
- **Endpoint**: `http://tempo:4317` (internal Docker network)
- **Sampler**: AlwaysOn (captures 100% of traces)

### Tempo Service:
- **HTTP API**: http://localhost:3200 (API only, no web UI)
- **Web UI**: Access traces through Grafana at http://localhost:3000
- **OTLP gRPC Receiver**: Internal port 4317 (accessible via `tempo:4317`)
- **OTLP HTTP Receiver**: Internal port 4318 (accessible via `tempo:4318`)
- **Storage**: Local filesystem (ephemeral, not production-ready)

---

## 🎯 Next Steps

1. ✅ Open Grafana and explore traces
2. Create more API traffic to generate traces
3. Try different TraceQL queries
4. Correlate traces with metrics and logs
5. Add custom spans for specific operations:

```csharp
using System.Diagnostics;

var activity = Activity.Current;
activity?.SetTag("custom.tag", "value");
activity?.AddEvent(new ActivityEvent("CustomEvent"));
```

6. For production:
   - Use a persistent Tempo backend (S3, GCS, Azure Blob)
   - Implement sampling strategies (not AlwaysOn)
   - Set up Tempo retention policies

---

## 📚 Resources

- **Grafana Tempo Docs**: https://grafana.com/docs/tempo/latest/
- **TraceQL Guide**: https://grafana.com/docs/tempo/latest/traceql/
- **OpenTelemetry .NET**: https://opentelemetry.io/docs/languages/net/
- **W3C Trace Context**: https://www.w3.org/TR/trace-context/

---

## 🐛 Troubleshooting

**No traces appearing?**
1. Check if Tempo is running: `docker-compose ps`
2. Verify Tempo logs: `docker logs tempo`
3. Ensure API is sending traces: `docker logs web-api | Select-String "OpenTelemetry"`
4. Check Grafana datasource: Configuration → Data Sources → Tempo → Test

**Tempo container restarting?**
- Fixed with `user: root` in docker-compose.yml
- Tempo needs write access to `/tmp/tempo`

**Old OpenTelemetry package warnings?**
- Packages 1.6.0 have known vulnerabilities
- Consider upgrading to latest stable versions in production
