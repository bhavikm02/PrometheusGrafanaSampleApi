# Quick Start: Viewing Traces

## ✅ Tempo is Running (Fixed!)

The 404 error at http://localhost:3200 is **expected** - Tempo doesn't have a web UI. You must view traces through **Grafana**.

---

## 🎯 How to View Traces (Step-by-Step)

### 1. Open Grafana
- **URL**: http://localhost:3000
- **Login**: `admin` / `admin`

### 2. Navigate to Explore
- Click the **Explore** icon (compass) in the left sidebar
- Or go directly to: http://localhost:3000/explore

### 3. Select Tempo Datasource
- At the top of the page, click the dropdown (says "Prometheus" or "Loki")
- Select **"Tempo"** from the list

### 4. Search for Traces
- Click the **"Search"** tab
- In **Service Name**, type: `PrometheusGrafanaSampleApi`
- Click **"Run query"** button (or press Shift+Enter)

### 5. View a Trace
- Click on any trace in the results
- See the detailed span timeline, tags, and timing information

---

## 🧪 Generate Test Traffic

```powershell
# Generate 10 requests to create traces
1..10 | ForEach-Object { 
    Invoke-WebRequest -Uri "http://localhost:8080/api/todos" -UseBasicParsing | Out-Null
    Start-Sleep -Milliseconds 200
}
```

Then refresh your Grafana Explore search to see the new traces!

---

## 🔍 What Traces Show

Each trace displays:
- **Request path** (e.g., `GET /api/todos`)
- **HTTP status code** (200, 404, 500, etc.)
- **Duration** (how long the request took)
- **Spans** (individual operations within the request)
- **Tags/Attributes** (HTTP method, route, server info)

---

## 📍 Direct API Access (Advanced)

Tempo's HTTP API is available at http://localhost:3200 for programmatic access:

### Check if Tempo is ready:
```powershell
Invoke-WebRequest -Uri "http://localhost:3200/ready"
```

### Search for traces via API:
```powershell
Invoke-WebRequest -Uri "http://localhost:3200/api/search?tags=service.name%3DPrometheusGrafanaSampleApi" | 
    Select-Object -ExpandProperty Content | ConvertFrom-Json
```

### Get a specific trace by ID:
```powershell
# Replace TRACE_ID with actual trace ID from search results
Invoke-WebRequest -Uri "http://localhost:3200/api/traces/TRACE_ID" | 
    Select-Object -ExpandProperty Content
```

But remember: **use Grafana for visualization** - the API is just for programmatic access!

---

## ✅ Verification Checklist

- [x] Tempo service is running (`docker-compose ps` shows "Up")
- [x] OTLP receivers are listening on ports 4317 (gRPC) and 4318 (HTTP)
- [x] Grafana has Tempo datasource configured
- [x] API is sending traces to Tempo
- [x] **Grafana is accessible** at http://localhost:3000
- [ ] **YOU**: View traces in Grafana Explore!

---

## 🚨 Troubleshooting

**"No data" in Grafana Explore?**
1. Make sure you selected "Tempo" datasource (top of page)
2. Generate traffic: `Invoke-WebRequest http://localhost:8080/api/todos`
3. Wait 2-3 seconds for traces to be indexed
4. Try searching again

**"Data source not found" error?**
- Grafana may still be loading - wait 10 seconds and refresh
- Check: http://localhost:3000/connections/datasources
- You should see "Tempo" in the list

**Want to see the raw data?**
```powershell
# Check Tempo API directly
Invoke-WebRequest -Uri "http://localhost:3200/api/search?tags=service.name%3DPrometheusGrafanaSampleApi" | 
    Select-Object -ExpandProperty Content | ConvertFrom-Json | ConvertTo-Json -Depth 10
```
