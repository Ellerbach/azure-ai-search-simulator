# Azure AI Search Simulator - Dockerfile
# Multi-stage build for optimal image size

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for restore
COPY AzureAISearchSimulator.sln ./
COPY src/AzureAISearchSimulator.Core/AzureAISearchSimulator.Core.csproj src/AzureAISearchSimulator.Core/
COPY src/AzureAISearchSimulator.Storage/AzureAISearchSimulator.Storage.csproj src/AzureAISearchSimulator.Storage/
COPY src/AzureAISearchSimulator.Search/AzureAISearchSimulator.Search.csproj src/AzureAISearchSimulator.Search/
COPY src/AzureAISearchSimulator.Indexing/AzureAISearchSimulator.Indexing.csproj src/AzureAISearchSimulator.Indexing/
COPY src/AzureAISearchSimulator.DataSources/AzureAISearchSimulator.DataSources.csproj src/AzureAISearchSimulator.DataSources/
COPY src/AzureAISearchSimulator.Api/AzureAISearchSimulator.Api.csproj src/AzureAISearchSimulator.Api/

# Restore dependencies
RUN dotnet restore src/AzureAISearchSimulator.Api/AzureAISearchSimulator.Api.csproj

# Copy source code
COPY src/ src/

# Build and publish
WORKDIR /src/src/AzureAISearchSimulator.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user for security (handle existing GID/UID)
RUN getent group 1000 || groupadd --gid 1000 appuser && \
    id -u 1000 >/dev/null 2>&1 || useradd --uid 1000 --gid 1000 --shell /bin/bash --create-home appuser

# Create directories for data persistence
RUN mkdir -p /app/data /app/lucene-indexes /app/certs && \
    chown -R 1000:1000 /app

# Generate self-signed certificate for HTTPS
RUN openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
    -keyout /app/certs/dev-cert.key \
    -out /app/certs/dev-cert.crt \
    -subj "/CN=localhost/O=AzureAISearchSimulator/C=US" && \
    openssl pkcs12 -export -out /app/certs/dev-cert.pfx \
    -inkey /app/certs/dev-cert.key \
    -in /app/certs/dev-cert.crt \
    -password pass:dev-password && \
    chown -R 1000:1000 /app/certs

# Copy published application
COPY --from=build /app/publish .

# Set environment variables
# HTTPS on 8443, HTTP on 8080
ENV ASPNETCORE_URLS=https://+:8443;http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_Kestrel__Certificates__Default__Path=/app/certs/dev-cert.pfx
ENV ASPNETCORE_Kestrel__Certificates__Default__Password=dev-password
ENV Simulator__DataPath=/app/data
ENV Lucene__IndexPath=/app/lucene-indexes

# Switch to non-root user
USER 1000

# Expose ports (HTTP and HTTPS)
EXPOSE 8080
EXPOSE 8443

# Health check (using HTTP endpoint)
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Entry point
ENTRYPOINT ["dotnet", "AzureAISearchSimulator.Api.dll"]
