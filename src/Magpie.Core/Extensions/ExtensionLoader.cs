using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

using Magpie.Models.Data;
using Magpie.Sdk.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Extensions;

internal sealed class ExtensionLoader(ILogger<ExtensionLoader> logger)
{
    internal static readonly Version CurrentAbiVersion =
        typeof(IExtension).Assembly.GetName().Version!.Normalize();

    internal static readonly string SdkAssemblyName = typeof(IExtension).Assembly.GetName().Name!;

    private static Type ResolveExtensionType(Assembly assembly)
    {
        var extensionTypes = (
            from type in assembly.GetTypes()
            where type is { IsPublic: true, IsAbstract: false, IsInterface: false }
            where typeof(IExtension).IsAssignableFrom(type)
            select type).ToList();

        return extensionTypes switch
        {
            [var single] => single,
            [] => throw new InvalidOperationException(
                $"Assembly {assembly.FullName} declares no public IExtension type."),
            _ => throw new InvalidOperationException(
                $"Assembly {assembly.FullName} declares multiple IExtension types: " +
                $"{string.Join(", ", extensionTypes.Select(t => t.FullName))}."),
        };
    }

    public IReadOnlyList<IExtension> LoadExtensions(
        IEnumerable<Extension> extensions,
        IServiceCollection services,
        ICollection<Extension>? loadFailures = null)
    {
        var loaded = new List<IExtension>();

        foreach (var extension in extensions)
        {
            try
            {
                loaded.Add(LoadExtension(extension, services));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "The extension loader failed to load extension {PackageId} from {FilePath} and skipped it",
                    extension.PackageId,
                    extension.FilePath);
                loadFailures?.Add(extension);
            }
        }

        return loaded;
    }

    public IReadOnlyList<IExtension> LoadBuiltInExtensions(
        IEnumerable<IExtension> builtIns,
        IServiceCollection services)
    {
        var loaded = new List<IExtension>();

        foreach (var instance in builtIns)
        {
            try
            {
                var assembly = instance.GetType().Assembly;
                loaded.Add(ActivateExtension(instance, assembly.Location, services));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "The extension loader failed to load built-in extension {ExtensionName} and skipped it",
                    instance.Name);
            }
        }

        return loaded;
    }

    private IExtension LoadExtension(Extension extension, IServiceCollection services)
    {
        if (!File.Exists(extension.FilePath))
        {
            throw new FileNotFoundException("Extension assembly not found.", extension.FilePath);
        }

        var assemblyPath = Path.GetFullPath(extension.FilePath);
        var context = new ExtensionAssemblyLoadContext(assemblyPath);
        var assembly = context.LoadWithoutLockingFile(assemblyPath);

        var builtAgainst = assembly.GetReferencedAssemblies()
            .FirstOrDefault(reference => reference.Name == SdkAssemblyName)?.Version
            ?? throw new InvalidOperationException(
                $"Assembly {assembly.FullName} does not reference {SdkAssemblyName}.");
        if (!CurrentAbiVersion.IsCompatibleWith(builtAgainst, targetAbi: null))
        {
            throw new InvalidOperationException(
                $"Extension {extension.PackageId} is built against ABI {builtAgainst.Format()}, which is not " +
                $"compatible with host ABI {CurrentAbiVersion.Format()}.");
        }

        var extensionType = ResolveExtensionType(assembly);
        var instance = (IExtension?)Activator.CreateInstance(extensionType)
            ?? throw new InvalidOperationException($"Activator returned null for {extensionType.FullName}.");
        if (!CurrentAbiVersion.IsCompatibleWith(builtAgainst, instance.TargetABI))
        {
            throw new InvalidOperationException(
                $"Extension {instance.Name} targets ABI {instance.TargetABI}, which is not compatible with host ABI " +
                $"{CurrentAbiVersion.Format()}.");
        }

        return ActivateExtension(instance, assemblyPath, services);
    }

    private IExtension ActivateExtension(IExtension instance, string source, IServiceCollection services)
    {
        instance.RegisterServices(services);

        logger.LogInformation(
            "The extension loader loaded extension {ExtensionName} v{ExtensionVersion} from {FilePath}",
            instance.Name,
            instance.Version,
            source);

        return instance;
    }

    internal sealed class ExtensionAssemblyLoadContext(string mainAssemblyPath) :
        AssemblyLoadContext(isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);
        private readonly string _extensionDirectory = Path.GetDirectoryName(mainAssemblyPath)!;

        public string MainAssemblyPath { get; } = mainAssemblyPath;

        public Assembly LoadWithoutLockingFile(string assemblyPath)
        {
            using var assemblyStream = new MemoryStream(File.ReadAllBytes(assemblyPath));
            var symbolPath = Path.ChangeExtension(assemblyPath, ".pdb");
            if (!File.Exists(symbolPath))
            {
                return LoadFromStream(assemblyStream);
            }

            using var symbolStream = new MemoryStream(File.ReadAllBytes(symbolPath));
            return LoadFromStream(assemblyStream, symbolStream);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch (FileNotFoundException)
            {
            }
            catch (FileLoadException)
            {
            }

            var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            if (resolved is not null)
            {
                return LoadWithoutLockingFile(resolved);
            }

            if (assemblyName.Name is { } simpleName)
            {
                var probe = Path.Combine(_extensionDirectory, simpleName + ".dll");
                if (File.Exists(probe))
                {
                    return LoadWithoutLockingFile(probe);
                }
            }

            return null;
        }
    }
}
