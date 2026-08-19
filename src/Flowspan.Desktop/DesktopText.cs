using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using Avalonia.Controls;

namespace Flowspan.Desktop;

public static class DesktopText
{
    private const string ResourcePrefix = "Flowspan.Desktop.Resources.";
    private const string ResourceSuffix = ".resources";

    private static readonly ResourceManager[] ResourceManagers =
        CreateResourceManagers();

    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        string? value = null;
        foreach (ResourceManager resourceManager in ResourceManagers)
        {
            string? candidate = resourceManager.GetString(
                key,
                CultureInfo.CurrentUICulture);
            if (candidate is null)
            {
                continue;
            }

            if (value is not null)
            {
                throw new InvalidOperationException(
                    $"Desktop resource '{key}' is defined more than once.");
            }

            value = candidate;
        }

        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Desktop resource '{key}' is missing or blank.")
            : value;
    }

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    public static void AddTo(IResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        foreach (string key in GetNeutralKeys())
        {
            resources.Add(key, Get(key));
        }
    }

    private static ResourceManager[] CreateResourceManagers()
    {
        Assembly assembly = typeof(DesktopText).Assembly;
        ResourceManager[] managers = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(
                    ResourcePrefix,
                    StringComparison.Ordinal)
                && name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name => new ResourceManager(
                name[..^ResourceSuffix.Length],
                assembly))
            .ToArray();
        return managers.Length == 0
            ? throw new InvalidOperationException(
                "The neutral Desktop resource catalog is unavailable.")
            : managers;
    }

    private static IEnumerable<string> GetNeutralKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (ResourceManager resourceManager in ResourceManagers)
        {
            ResourceSet resourceSet = resourceManager.GetResourceSet(
                CultureInfo.InvariantCulture,
                createIfNotExists: true,
                tryParents: true)
            ?? throw new InvalidOperationException(
                "The neutral Desktop resource catalog is unavailable.");

            foreach (DictionaryEntry entry in resourceSet)
            {
                if (entry.Key is not string key || !keys.Add(key))
                {
                    throw new InvalidOperationException(
                        $"Desktop resource '{entry.Key}' is defined more than once.");
                }

                yield return key;
            }
        }
    }
}
