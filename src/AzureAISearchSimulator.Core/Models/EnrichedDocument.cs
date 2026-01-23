using System.Text.Json;
using System.Text.Json.Nodes;

namespace AzureAISearchSimulator.Core.Models;

/// <summary>
/// Represents a document being enriched by the skill pipeline.
/// This is a tree structure where skills can read from and write to paths like "/document/content".
/// </summary>
public class EnrichedDocument
{
    private readonly JsonNode _root;

    /// <summary>
    /// Creates a new enriched document with an empty document node.
    /// </summary>
    public EnrichedDocument()
    {
        _root = new JsonObject
        {
            ["document"] = new JsonObject()
        };
    }

    /// <summary>
    /// Creates an enriched document from existing content.
    /// </summary>
    public EnrichedDocument(Dictionary<string, object?> initialContent)
    {
        var documentNode = new JsonObject();
        foreach (var (key, value) in initialContent)
        {
            documentNode[key] = JsonSerializer.SerializeToNode(value);
        }
        _root = new JsonObject
        {
            ["document"] = documentNode
        };
    }

    /// <summary>
    /// Gets a value at the specified path (e.g., "/document/content").
    /// </summary>
    public object? GetValue(string path)
    {
        var node = GetNode(path);
        return ConvertNodeToObject(node);
    }

    /// <summary>
    /// Gets a strongly-typed value at the specified path.
    /// </summary>
    public T? GetValue<T>(string path)
    {
        var node = GetNode(path);
        if (node == null) return default;
        return node.Deserialize<T>();
    }

    /// <summary>
    /// Sets a value at the specified path.
    /// </summary>
    public void SetValue(string path, object? value)
    {
        var segments = ParsePath(path);
        if (segments.Length == 0) return;

        JsonNode current = _root;
        
        // Navigate to parent, creating nodes as needed
        for (int i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (int.TryParse(segment, out int index))
            {
                // Array access
                if (current is JsonArray arr)
                {
                    while (arr.Count <= index)
                    {
                        arr.Add(new JsonObject());
                    }
                    current = arr[index] ?? new JsonObject();
                }
            }
            else
            {
                // Object access
                if (current is JsonObject obj)
                {
                    if (!obj.ContainsKey(segment) || obj[segment] == null)
                    {
                        obj[segment] = new JsonObject();
                    }
                    current = obj[segment]!;
                }
            }
        }

        // Set the final value
        var lastSegment = segments[^1];
        if (current is JsonObject finalObj)
        {
            finalObj[lastSegment] = JsonSerializer.SerializeToNode(value);
        }
        else if (current is JsonArray finalArr && int.TryParse(lastSegment, out int idx))
        {
            while (finalArr.Count <= idx)
            {
                finalArr.Add(null);
            }
            finalArr[idx] = JsonSerializer.SerializeToNode(value);
        }
    }

    /// <summary>
    /// Checks if a path exists in the document.
    /// </summary>
    public bool HasValue(string path)
    {
        return GetNode(path) != null;
    }

    /// <summary>
    /// Gets all paths matching a wildcard pattern like "/document/pages/*".
    /// </summary>
    public IEnumerable<string> GetMatchingPaths(string pattern)
    {
        if (!pattern.Contains('*'))
        {
            if (HasValue(pattern))
                yield return pattern;
            yield break;
        }

        var parts = pattern.Split('/').Where(p => !string.IsNullOrEmpty(p)).ToArray();
        var results = new List<string>();
        CollectMatchingPaths(_root, parts, 0, "", results);
        
        foreach (var result in results)
        {
            yield return result;
        }
    }

    /// <summary>
    /// Converts the enriched document to a dictionary.
    /// </summary>
    public Dictionary<string, object?> ToDictionary()
    {
        var documentNode = _root["document"];
        if (documentNode == null) return new Dictionary<string, object?>();
        
        var result = new Dictionary<string, object?>();
        if (documentNode is JsonObject obj)
        {
            foreach (var property in obj)
            {
                result[property.Key] = ConvertNodeToObject(property.Value);
            }
        }
        return result;
    }

    /// <summary>
    /// Gets the raw JSON representation.
    /// </summary>
    public string ToJson()
    {
        return _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private JsonNode? GetNode(string path)
    {
        var segments = ParsePath(path);
        JsonNode? current = _root;

        foreach (var segment in segments)
        {
            if (current == null) return null;

            if (int.TryParse(segment, out int index))
            {
                // Array access
                if (current is JsonArray arr && index >= 0 && index < arr.Count)
                {
                    current = arr[index];
                }
                else
                {
                    return null;
                }
            }
            else
            {
                // Object access
                if (current is JsonObject obj && obj.ContainsKey(segment))
                {
                    current = obj[segment];
                }
                else
                {
                    return null;
                }
            }
        }

        return current;
    }

    private static string[] ParsePath(string path)
    {
        return path.Split('/')
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }

    private void CollectMatchingPaths(JsonNode? current, string[] parts, int partIndex, string currentPath, List<string> results)
    {
        if (current == null) return;
        
        if (partIndex >= parts.Length)
        {
            results.Add(currentPath);
            return;
        }

        var part = parts[partIndex];

        if (part == "*")
        {
            // Wildcard - expand to all children
            if (current is JsonArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    CollectMatchingPaths(arr[i], parts, partIndex + 1, $"{currentPath}/{i}", results);
                }
            }
            else if (current is JsonObject obj)
            {
                foreach (var property in obj)
                {
                    CollectMatchingPaths(property.Value, parts, partIndex + 1, $"{currentPath}/{property.Key}", results);
                }
            }
        }
        else
        {
            // Exact match
            if (current is JsonObject obj && obj.ContainsKey(part))
            {
                CollectMatchingPaths(obj[part], parts, partIndex + 1, $"{currentPath}/{part}", results);
            }
            else if (current is JsonArray arr && int.TryParse(part, out int index) && index < arr.Count)
            {
                CollectMatchingPaths(arr[index], parts, partIndex + 1, $"{currentPath}/{part}", results);
            }
        }
    }

    private static object? ConvertNodeToObject(JsonNode? node)
    {
        if (node == null) return null;

        return node switch
        {
            JsonValue value => value.GetValue<object>(),
            JsonArray array => array.Select(ConvertNodeToObject).ToList(),
            JsonObject obj => obj.ToDictionary(p => p.Key, p => ConvertNodeToObject(p.Value)),
            _ => null
        };
    }
}
