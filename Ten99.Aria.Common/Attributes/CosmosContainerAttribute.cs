using System.Reflection;
using System.Text.Json.Serialization;

namespace Ten99.Aria.Common.Attributes;

/// <summary>
/// Declares the Cosmos DB container name and partition key paths for a model class.
/// Paths reference JSON property names (e.g. "/organization", "/taskId").
///
/// Lives in Common (not Data.Cosmos) so model assemblies can annotate their types
/// without taking a dependency on a specific data-layer implementation.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CosmosContainerAttribute(string name, params string[] partitionKeyPaths) : Attribute
{
    public string Name { get; } = name;
    public string[] PartitionKeyPaths { get; } = partitionKeyPaths;

    public static string GetName<T>() =>
        GetAttribute<T>().Name;

    public static string[] GetPartitionKeyPaths<T>() =>
        GetAttribute<T>().PartitionKeyPaths;

    public static PropertyInfo[] GetPartitionKeyProperties<T>()
    {
        var paths = GetPartitionKeyPaths<T>();
        var properties = typeof(T).GetProperties();
        var result = new PropertyInfo[paths.Length];

        for (var i = 0; i < paths.Length; i++)
        {
            var jsonName = paths[i].TrimStart('/');
            var prop = properties.FirstOrDefault(p =>
                p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == jsonName)
                ?? throw new InvalidOperationException($"{typeof(T).Name} has no property with [JsonPropertyName(\"{jsonName}\")] matching partition key path '{paths[i]}'.");
            result[i] = prop;
        }

        return result;
    }

    private static CosmosContainerAttribute GetAttribute<T>() =>
        typeof(T).GetCustomAttribute<CosmosContainerAttribute>()
        ?? throw new InvalidOperationException($"{typeof(T).Name} is missing the [CosmosContainer] attribute.");
}
