# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["PrometheusGrafanaSampleApi.csproj", "./"]
RUN dotnet restore "PrometheusGrafanaSampleApi.csproj" --verbosity normal

# Copy everything else and build
COPY . .
RUN dotnet restore "PrometheusGrafanaSampleApi.csproj" --verbosity normal
RUN dotnet build "PrometheusGrafanaSampleApi.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "PrometheusGrafanaSampleApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080

# Copy published app
COPY --from=publish /app/publish .

# Set environment variable for port
ENV PORT=8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

ENTRYPOINT ["dotnet", "PrometheusGrafanaSampleApi.dll"]

