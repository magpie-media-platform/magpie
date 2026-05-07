using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

using Magpie.Abstractions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Magpie.App.Extensions;

/// <summary>
/// Discovers extension assemblies on disk, instantiates the <see cref="IExtension"/>
/// types they expose, and lets each one register services with the host.
/// </summary>
/// <remarks>
/// <para>
/// The loader is resilient: a single broken extension (missing dependency, throwing
/// constructor, throwing <see cref="IExtension.RegisterServices"/>) is logged and
/// skipped. The host continues with whatever extensions did load successfully.
/// </para>
/// <para>
/// Each extension assembly must declare exactly one <see cref="IExtension"/> type.
/// An assembly that declares more than one is logged and skipped entirely rather
/// than picking one arbitrarily.
/// </para>
/// </remarks>
public sealed class ExtensionLoader
{
    private readonly ILogger<ExtensionLoader> _logger;

    /// <summary>Initializes a new instance.</summary>
    public ExtensionLoader(ILogger<ExtensionLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Discovers, instantiates, and registers extensions found under <paramref name="extensionsPath"/>.
    /// </summary>
    /// <param name="extensionsPath">The root directory containing extension subdirectories.</param>
    /// <param name="services">The service collection that extensions register their services with.</param>
    /// <returns>The set of successfully loaded extensions.</returns>
    public IReadOnlyList<LoadedExtension> LoadExtensions(string extensionsPath, IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(extensionsPath))
        {
            _logger.LogDebug("Extensions path is empty; no extensions will be loaded.");
            return [];
        }

        if (!Directory.Exists(extensionsPath))
        {
            _logger.LogInformation(
                "Extensions directory '{ExtensionsPath}' does not exist; no extensions will be loaded.",
                extensionsPath);
            return [];
        }

        var loaded = new List<LoadedExtension>();

        foreach (var directory in Directory.EnumerateDirectories(extensionsPath))
        {
            var directoryName = Path.GetFileName(directory);

            try
            {
                var assemblyPath = ResolveAssemblyPath(directory, directoryName);
                if (assemblyPath is null)
                {
                    _logger.LogWarning(
                        "No candidate extension assembly found in '{ExtensionDirectory}'.",
                        directory);
                    continue;
                }

                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);

                var extensionTypes = DiscoverExtensionTypes(assembly).ToList();
                if (extensionTypes.Count > 1)
                {
                    _logger.LogWarning(
                        "Assembly '{AssemblyPath}' declares multiple IExtension types ({ExtensionTypes}). " +
                        "Skipping the entire assembly; an extension assembly must declare exactly one IExtension.",
                        assemblyPath,
                        string.Join(", ", extensionTypes.Select(t => t.FullName)));
                    continue;
                }

                foreach (var extensionType in extensionTypes)
                {
                    if (TryInstantiate(extensionType, out var instance) && instance is not null)
                    {
                        if (TryRegister(instance, services))
                        {
                            loaded.Add(new LoadedExtension(instance, assembly, directory));
                            _logger.LogInformation(
                                "Loaded extension '{ExtensionName}' v{ExtensionVersion} from '{AssemblyPath}'.",
                                instance.Name,
                                instance.Version,
                                assemblyPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to load extension from directory '{ExtensionDirectory}'.",
                    directory);
            }
        }

        return loaded;
    }

    private static string? ResolveAssemblyPath(string directory, string directoryName)
    {
        // Prefer an assembly that matches the directory name.
        var preferred = Path.Combine(directory, $"{directoryName}.dll");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        // Fallback: return the first .dll; DiscoverExtensionTypes filters by IExtension later.
        // TODO: scan all DLLs and pick the one that actually contains an IExtension type
        // rather than the first arbitrary match -- the current approach can pick a
        // transitive dependency.
        return Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
    }

    private IEnumerable<Type> DiscoverExtensionTypes(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            _logger.LogWarning(
                ex,
                "Some types failed to load from assembly '{AssemblyName}'. Continuing with the types that did load.",
                assembly.FullName);
            types = [.. ex.Types.Where(t => t is not null).Cast<Type>()];
        }

        foreach (var type in types)
        {
            if (type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            if (!typeof(IExtension).IsAssignableFrom(type))
            {
                continue;
            }

            yield return type;
        }
    }

    private bool TryInstantiate(Type extensionType, out IExtension? instance)
    {
        try
        {
            instance = (IExtension?)Activator.CreateInstance(extensionType);
            if (instance is null)
            {
                _logger.LogWarning(
                    "Activator returned null for extension type '{ExtensionType}'.",
                    extensionType.FullName);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to instantiate extension type '{ExtensionType}'.",
                extensionType.FullName);
            instance = null;
            return false;
        }
    }

    private bool TryRegister(IExtension extension, IServiceCollection services)
    {
        try
        {
            extension.RegisterServices(services);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Extension '{ExtensionName}' threw during RegisterServices. It will be skipped.",
                extension.Name);
            return false;
        }
    }
}
