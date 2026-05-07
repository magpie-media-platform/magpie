using System.Reflection;

using Magpie.Abstractions;

namespace Magpie.App.Extensions;

/// <summary>
/// Information about an extension that has been successfully discovered and instantiated
/// by the <see cref="ExtensionLoader"/>.
/// </summary>
/// <param name="Instance">The extension instance.</param>
/// <param name="Assembly">The assembly the extension was loaded from.</param>
/// <param name="DataDirectory">The on-disk directory the extension was loaded from.</param>
public sealed record LoadedExtension(IExtension Instance, Assembly Assembly, string DataDirectory);
