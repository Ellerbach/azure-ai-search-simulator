# Contributing to Azure AI Search Simulator

Thank you for your interest in contributing to the Azure AI Search Simulator! This document provides guidelines and information for contributors.

## Code of Conduct

This project adheres to a code of conduct. By participating, you are expected to uphold this code. Please be respectful and constructive in all interactions.

## How to Contribute

### Reporting Bugs

Before creating a bug report, please check if the issue has already been reported. When creating a bug report, include:

- **Clear title** describing the issue
- **Steps to reproduce** the problem
- **Expected behavior** vs **actual behavior**
- **Environment details** (.NET version, OS, etc.)
- **Relevant logs or error messages**
- **API request/response** examples if applicable

### Suggesting Features

Feature suggestions are welcome! Please include:

- **Clear description** of the feature
- **Use case** - why is this feature needed?
- **Azure AI Search reference** - link to the official documentation if applicable
- **Proposed implementation** (optional)

### Pull Requests

1. **Fork the repository** and create your branch from `main`
2. **Follow the coding standards** (see below)
3. **Add tests** for new functionality
4. **Update documentation** as needed
5. **Ensure all tests pass** before submitting
6. **Write a clear PR description** explaining your changes

## Development Setup

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022, VS Code, or JetBrains Rider
- Git

### Building the Project

```bash
# Clone your fork
git clone https://github.com/your-username/azure-ai-search-simulator.git
cd azure-ai-search-simulator

# Build the solution
dotnet build

# Run tests
dotnet test

# Run the API
dotnet run --project src/AzureAISearchSimulator.Api
```

### Project Structure

```
├── src/
│   ├── AzureAISearchSimulator.Api/        # ASP.NET Core Web API
│   ├── AzureAISearchSimulator.Core/       # Core models and interfaces
│   ├── AzureAISearchSimulator.Search/     # Lucene.NET search & skills
│   ├── AzureAISearchSimulator.Storage/    # LiteDB persistence
│   └── AzureAISearchSimulator.DataSources/# Azure data source connectors
├── tests/
│   ├── AzureAISearchSimulator.Core.Tests/
│   ├── AzureAISearchSimulator.Api.Tests/
│   └── AzureAISearchSimulator.Integration.Tests/
├── samples/
│   └── AzureSdkSample/                    # Azure SDK compatibility sample
└── docs/                                   # Documentation
```

## Coding Standards

### C# Guidelines

- Follow [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful names for variables, methods, and classes
- Keep methods small and focused (single responsibility)
- Use async/await for I/O operations
- Add XML documentation for public APIs

### Code Style

- Use 4 spaces for indentation (no tabs)
- Place opening braces on new lines
- Use `var` when the type is obvious from the right side
- Prefer expression-bodied members for simple properties/methods
- Use nullable reference types (`#nullable enable`)

### Example

```csharp
/// <summary>
/// Creates a new search index.
/// </summary>
/// <param name="index">The index definition.</param>
/// <returns>The created index.</returns>
/// <exception cref="ArgumentException">Thrown when index name is invalid.</exception>
public async Task<SearchIndex> CreateAsync(SearchIndex index)
{
    if (string.IsNullOrWhiteSpace(index.Name))
    {
        throw new ArgumentException("Index name is required.", nameof(index));
    }

    _logger.LogInformation("Creating index: {IndexName}", index.Name);
    
    return await _repository.CreateAsync(index);
}
```

### Testing Guidelines

- Write unit tests for all new functionality
- Use xUnit for testing
- Follow the Arrange-Act-Assert pattern
- Use descriptive test names: `MethodName_Scenario_ExpectedResult`
- Mock external dependencies

```csharp
[Fact]
public async Task CreateAsync_WithValidIndex_ReturnsCreatedIndex()
{
    // Arrange
    var index = new SearchIndex { Name = "test-index" };
    
    // Act
    var result = await _service.CreateAsync(index);
    
    // Assert
    Assert.NotNull(result);
    Assert.Equal("test-index", result.Name);
}
```

## Areas for Contribution

### High Priority

- [ ] Additional cognitive skills (Entity Recognition, Key Phrase Extraction)
- [ ] Azure SQL data source connector
- [ ] Cosmos DB data source connector
- [ ] Scoring profiles implementation
- [ ] Synonym maps

### Medium Priority

- [ ] HNSW algorithm optimization for vector search
- [ ] More language analyzers
- [ ] Admin web UI dashboard
- [ ] Performance benchmarking suite

### Documentation

- Improve API documentation
- Add more code examples
- Write tutorials
- Translate documentation

## Commit Messages

Use clear and descriptive commit messages:

```text
feat: Add Azure Blob Storage connector

- Implement AzureBlobStorageConnector with connection string auth
- Add Managed Identity support via DefaultAzureCredential
- Add blob prefix filtering via container query
- Include unit tests for connector

Closes #123
```

Prefixes:

- `feat:` - New feature
- `fix:` - Bug fix
- `docs:` - Documentation changes
- `test:` - Adding or updating tests
- `refactor:` - Code refactoring
- `chore:` - Build/CI changes

## Questions?

If you have questions about contributing, feel free to:

- Open a GitHub Discussion
- Create an issue with the "question" label

Thank you for contributing! 🎉
