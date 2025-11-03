# Prometheus Grafana Sample API

A comprehensive .NET 8 Web API demonstrating full-stack observability with Prometheus metrics, Grafana Loki logs, and Grafana Tempo distributed tracing.

## 🚀 Features

- **RESTful API**: Simple Todo API built with ASP.NET Core 8.0
- **Metrics Collection**: Prometheus integration for monitoring HTTP requests, response times, and custom metrics
- **Distributed Tracing**: OpenTelemetry with Grafana Tempo for distributed tracing
- **Log Aggregation**: Grafana Loki with Promtail for centralized logging
- **Visualization**: Grafana dashboards with pre-configured datasources
- **Containerization**: Docker Compose setup for local development
- **Cloud-Ready**: EKS deployment guide for production workloads

## 📋 Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://www.docker.com/get-started) and Docker Compose
- (Optional) Visual Studio 2022 or VS Code

## 🏗️ Architecture

The project consists of the following components:

- **Web API** (.NET 8): Sample API with Entity Framework Core (In-Memory Database)
- **Prometheus**: Metrics collection and storage
- **Grafana**: Visualization and dashboarding
- **Loki**: Log aggregation system
- **Promtail**: Log shipping agent
- **Tempo**: Distributed tracing backend

## 🛠️ Getting Started

### Local Development with Docker Compose

1. **Clone the repository**
   ```bash
   git clone https://github.com/bhavikm02/PrometheusGrafanaSampleApi.git
   cd PrometheusGrafanaSampleApi
   ```

2. **Build and run all services**
   ```bash
   docker-compose up --build
   ```

3. **Access the services**
   - API: http://localhost:8080
   - Swagger UI: http://localhost:8080/swagger
   - Prometheus: http://localhost:9090
   - Grafana: http://localhost:3000 (admin/admin)
   - Tempo: http://localhost:3200

### Running Locally (without Docker)

1. **Restore dependencies**
   ```bash
   dotnet restore
   ```

2. **Run the application**
   ```bash
   dotnet run
   ```

The API will be available at `http://localhost:5000` or `https://localhost:5001`.

## 📡 API Endpoints

### Todos Controller

- `GET /api/todos` - Get all todos
- `GET /api/todos/{id}` - Get a specific todo by ID
- `POST /api/todos` - Create a new todo
- `PUT /api/todos/{id}` - Update an existing todo
- `DELETE /api/todos/{id}` - Delete a todo

### Monitoring Endpoints

- `GET /metrics` - Prometheus metrics endpoint

### Example Request

```bash
# Create a new todo
curl -X POST http://localhost:8080/api/todos \
  -H "Content-Type: application/json" \
  -d '{"title":"Learn Observability","isCompleted":false}'

# Get all todos
curl http://localhost:8080/api/todos
```

## 📊 Grafana Dashboards

The project includes pre-configured Grafana dashboards:

1. **API Overview Dashboard** - HTTP metrics, request rates, latencies, and error rates
2. **Logs Dashboard** - Application logs from Loki
3. **Traces Dashboard** - Distributed traces from Tempo

Access Grafana at `http://localhost:3000` with credentials: `admin/admin`

See [GRAFANA_DASHBOARDS.md](GRAFANA_DASHBOARDS.md) for detailed dashboard information.

## 🔍 Observability

### Metrics (Prometheus)

- HTTP request duration
- HTTP request count by status code
- Custom application metrics

View metrics at: http://localhost:9090

### Logs (Loki)

- Structured application logs
- Docker container logs
- Centralized log aggregation

Query logs in Grafana's Explore view.

### Traces (Tempo)

- Distributed tracing with OpenTelemetry
- Request flow visualization
- Performance bottleneck identification

View traces in Grafana's Explore view or linked from logs.

See [TRACING.md](TRACING.md) and [TRACING_QUICK_START.md](TRACING_QUICK_START.md) for detailed tracing setup.

## ☸️ Kubernetes Deployment

For deploying to Amazon EKS with full APM (Application Performance Monitoring) capabilities, refer to:

- [EKS_DEPLOYMENT_APM_GUIDE.md](EKS_DEPLOYMENT_APM_GUIDE.md)

This guide includes:
- EKS cluster setup
- ADOT Collector configuration
- Amazon Managed Prometheus integration
- Amazon Managed Grafana setup
- AWS X-Ray tracing

## 🔧 Configuration

### Environment Variables

- `PORT` - HTTP port for the API (default: 8080)
- `ASPNETCORE_ENVIRONMENT` - Environment name (Development/Production)
- `OTEL_SERVICE_NAME` - Service name for OpenTelemetry (default: PrometheusGrafanaSampleApi)
- `OTEL_EXPORTER_OTLP_ENDPOINT` - OTLP endpoint for traces (default: http://tempo:4317)

### Prometheus Configuration

Edit `prometheus/prometheus.yml` to modify scrape intervals, targets, or add new jobs.

### Grafana Configuration

- Datasources: `grafana/provisioning/datasources/datasources.yml`
- Dashboards: `grafana/provisioning/dashboards/json/`

## 🧪 Testing the Stack

1. **Generate some traffic**
   ```bash
   # Create multiple todos
   for i in {1..10}; do
     curl -X POST http://localhost:8080/api/todos \
       -H "Content-Type: application/json" \
       -d "{\"title\":\"Task $i\",\"isCompleted\":false}"
   done

   # Get all todos
   curl http://localhost:8080/api/todos
   ```

2. **View in Grafana**
   - Open http://localhost:3000
   - Navigate to Dashboards → API Overview
   - See real-time metrics, logs, and traces

## 🐳 Docker Images

The application uses multi-stage Docker builds for optimized image size:

```dockerfile
# Build stage with SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Runtime stage with minimal runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
```

## 📦 Dependencies

- **Microsoft.EntityFrameworkCore.InMemory** 8.0.0
- **prometheus-net.AspNetCore** 8.2.1
- **OpenTelemetry.Extensions.Hosting**
- **OpenTelemetry.Exporter.OpenTelemetryProtocol**
- **OpenTelemetry.Instrumentation.AspNetCore**
- **OpenTelemetry.Instrumentation.Http**
- **Swashbuckle.AspNetCore** 6.5.0

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## 📝 License

This project is open source and available under the [MIT License](LICENSE).

## 📚 Additional Resources

- [Prometheus Documentation](https://prometheus.io/docs/)
- [Grafana Documentation](https://grafana.com/docs/)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/instrumentation/net/)
- [Grafana Loki](https://grafana.com/docs/loki/latest/)
- [Grafana Tempo](https://grafana.com/docs/tempo/latest/)

## 🆘 Troubleshooting

### Issue: Containers won't start
- Ensure ports 8080, 3000, 9090, 3100, and 3200 are not in use
- Run `docker-compose down -v` to clean up volumes and try again

### Issue: No metrics in Prometheus
- Verify the API is running: `curl http://localhost:8080/metrics`
- Check Prometheus targets: http://localhost:9090/targets
- Ensure the web-api container is accessible on the observability network

### Issue: No logs in Loki
- Check Promtail configuration in `promtail/config.yml`
- Verify Docker socket is accessible to Promtail container
- Check Loki status in Grafana datasources

### Issue: No traces in Tempo
- Verify OTLP endpoint configuration
- Check Tempo is receiving data: http://localhost:3200
- Ensure OpenTelemetry SDK is properly configured

## 👨‍💻 Author

**Bhavik Makwana**

## ⭐ Show your support

Give a ⭐️ if this project helped you learn about observability!
