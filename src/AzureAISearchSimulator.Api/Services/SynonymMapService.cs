using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Storage.Repositories;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AzureAISearchSimulator.Api.Services;

/// <summary>
/// Service for managing synonym maps and resolving synonyms at query time.
/// Parses Apache Solr synonym format:
/// - Equivalent synonyms: "word1, word2, word3" (bidirectional)
/// - Explicit mappings: "word1, word2 => word3, word4" (unidirectional)
/// </summary>
public class SynonymMapService : ISynonymMapService, ISynonymMapResolver
{
    private readonly ISynonymMapRepository _repository;
    private readonly ILogger<SynonymMapService> _logger;

    /// <summary>
    /// Cached parsed synonym rules keyed by synonym map name.
    /// Each entry maps a term (lowercase) to the set of synonym terms it should expand to.
    /// </summary>
    private readonly ConcurrentDictionary<string, Dictionary<string, HashSet<string>>> _synonymCache = new();

    public SynonymMapService(
        ISynonymMapRepository repository,
        ILogger<SynonymMapService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<SynonymMap> CreateAsync(SynonymMap synonymMap, CancellationToken cancellationToken = default)
    {
        ValidateSynonymMap(synonymMap);

        if (await _repository.ExistsAsync(synonymMap.Name))
        {
            throw new InvalidOperationException($"Synonym map '{synonymMap.Name}' already exists");
        }

        _logger.LogInformation("Creating synonym map '{Name}'", synonymMap.Name);

        var result = await _repository.CreateAsync(synonymMap);
        InvalidateCache(synonymMap.Name);
        return result;
    }

    public async Task<SynonymMap?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _repository.GetByNameAsync(name);
    }

    public async Task<IEnumerable<SynonymMap>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _repository.GetAllAsync();
    }

    public async Task<SynonymMap> CreateOrUpdateAsync(string name, SynonymMap synonymMap, CancellationToken cancellationToken = default)
    {
        synonymMap.Name = name;
        ValidateSynonymMap(synonymMap);

        if (await _repository.ExistsAsync(name))
        {
            _logger.LogInformation("Updating existing synonym map '{Name}'", name);
            var result = await _repository.UpdateAsync(synonymMap);
            InvalidateCache(name);
            return result;
        }
        else
        {
            _logger.LogInformation("Creating new synonym map '{Name}'", name);
            var result = await _repository.CreateAsync(synonymMap);
            InvalidateCache(name);
            return result;
        }
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting synonym map '{Name}'", name);
        InvalidateCache(name);
        return await _repository.DeleteAsync(name);
    }

    public async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _repository.ExistsAsync(name);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await _repository.CountAsync();
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetSynonyms(string synonymMapName, string term)
    {
        var rules = GetOrLoadRules(synonymMapName);
        if (rules == null)
        {
            return new[] { term };
        }

        var normalizedTerm = term.Trim().ToLowerInvariant();
        if (rules.TryGetValue(normalizedTerm, out var synonyms))
        {
            // Return the original term plus all synonyms
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { term };
            foreach (var syn in synonyms)
            {
                result.Add(syn);
            }
            return result.ToList();
        }

        return new[] { term };
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ExpandTerms(IEnumerable<string> synonymMapNames, string term)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { term };

        foreach (var mapName in synonymMapNames)
        {
            var expanded = GetSynonyms(mapName, term);
            foreach (var t in expanded)
            {
                result.Add(t);
            }
        }

        return result.ToList();
    }

    private Dictionary<string, HashSet<string>>? GetOrLoadRules(string synonymMapName)
    {
        if (_synonymCache.TryGetValue(synonymMapName, out var cached))
        {
            return cached;
        }

        // Load from repository synchronously (synonym maps are small and cached)
        var synonymMap = _repository.GetByNameAsync(synonymMapName).GetAwaiter().GetResult();
        if (synonymMap == null)
        {
            _logger.LogWarning("Synonym map '{Name}' not found during query expansion", synonymMapName);
            return null;
        }

        var rules = ParseSynonymRules(synonymMap.Synonyms);
        _synonymCache[synonymMapName] = rules;
        return rules;
    }

    private void InvalidateCache(string synonymMapName)
    {
        _synonymCache.TryRemove(synonymMapName, out _);
    }

    /// <summary>
    /// Parses Solr-format synonym rules into a lookup dictionary.
    /// </summary>
    public static Dictionary<string, HashSet<string>> ParseSynonymRules(string synonyms)
    {
        var rules = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(synonyms))
        {
            return rules;
        }

        var lines = synonyms.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            {
                continue;
            }

            if (line.Contains("=>"))
            {
                // Explicit mapping: "word1, word2 => word3, word4"
                // Left-side terms map to right-side terms
                var parts = line.Split("=>", 2);
                var leftTerms = parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLowerInvariant())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();
                var rightTerms = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLowerInvariant())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                if (leftTerms.Count == 0 || rightTerms.Count == 0)
                {
                    continue;
                }

                // Each left-side term maps to all right-side terms
                foreach (var left in leftTerms)
                {
                    if (!rules.TryGetValue(left, out var existing))
                    {
                        existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        rules[left] = existing;
                    }
                    foreach (var right in rightTerms)
                    {
                        existing.Add(right);
                    }
                }
            }
            else
            {
                // Equivalent synonyms: "word1, word2, word3"
                // All terms map to all other terms (bidirectional)
                var terms = line.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLowerInvariant())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                if (terms.Count < 2)
                {
                    continue;
                }

                foreach (var term in terms)
                {
                    if (!rules.TryGetValue(term, out var existing))
                    {
                        existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        rules[term] = existing;
                    }
                    foreach (var other in terms)
                    {
                        if (!string.Equals(term, other, StringComparison.OrdinalIgnoreCase))
                        {
                            existing.Add(other);
                        }
                    }
                }
            }
        }

        return rules;
    }

    private void ValidateSynonymMap(SynonymMap synonymMap)
    {
        if (string.IsNullOrWhiteSpace(synonymMap.Name))
        {
            throw new ArgumentException("Synonym map name is required");
        }

        if (synonymMap.Name.Length > 128)
        {
            throw new ArgumentException("Synonym map name cannot exceed 128 characters");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(synonymMap.Name, @"^[a-zA-Z][a-zA-Z0-9\-]*$"))
        {
            throw new ArgumentException("Synonym map name must start with a letter and contain only letters, numbers, and hyphens");
        }

        if (!string.Equals(synonymMap.Format, "solr", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only 'solr' format is supported for synonym maps");
        }

        if (string.IsNullOrWhiteSpace(synonymMap.Synonyms))
        {
            throw new ArgumentException("Synonym map must contain at least one synonym rule");
        }

        // Validate that the rules can be parsed
        try
        {
            ParseSynonymRules(synonymMap.Synonyms);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid synonym rules: {ex.Message}");
        }
    }
}
