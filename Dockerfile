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

# Create non-root user for security
RUN groupadd --gid 1000 appuser && \
    useradd --uid 1000 --gid appuser --shell /bin/bash --create-home appuser

# Create directories for data persistence
RUN mkdir -p /app/data /app/lucene-indexes && \
    chown -R appuser:appuser /app

# Copy published application
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV Simulator__DataPath=/app/data
ENV Lucene__IndexPath=/app/lucene-indexes

# Switch to non-root user
USER appuser

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Entry point
ENTRYPOINT ["dotnet", "AzureAISearchSimulator.Api.dll"]
