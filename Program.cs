using Microsoft.EntityFrameworkCore;
using PrometheusGrafanaSampleApi.Data;
using PrometheusGrafanaSampleApi.Models;
using Prometheus;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddDbContext<TodoContext>(options =>
    options.UseInMemoryDatabase("TodosDb"));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure logging to console (stdout)
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// OpenTelemetry Tracing
var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? "PrometheusGrafanaSampleApi";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(rb => rb.AddService(serviceName))
    .WithTracing(tracer =>
    {
        tracer
            .SetSampler(new AlwaysOnSampler())
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
            })
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(otlp =>
            {
                var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://tempo:4317";
                otlp.Endpoint = new Uri(endpoint);
            });
    });

var app = builder.Build();

// Configure HTTP pipeline
app.UseSwagger();
app.UseSwaggerUI();

// Prometheus metrics
app.UseHttpMetrics();

app.UseRouting();
app.MapControllers();

// Map metrics endpoint
app.MapMetrics();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<TodoContext>();
    context.Database.EnsureCreated();
}

// Configure port for Docker
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://0.0.0.0:{port}");

app.Run();
