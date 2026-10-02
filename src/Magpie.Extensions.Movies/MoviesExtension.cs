using System;

using Magpie.Sdk.Content;
using Magpie.Sdk.Extensions;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.Movies;

/// <summary>
/// The Magpie extension for the Movie content type.
/// </summary>
public sealed class MoviesExtension : IExtensionLifecycle
{
    private static readonly Guid ExtensionId = new("9719FAC3-1DA3-4BA8-816F-28D0A8F1AF6F");

    /// <inheritdoc />
    public Guid Id => ExtensionId;

    /// <inheritdoc />
    public string Name => "Movies";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IContentTypeDefinitionProvider, MoviesContentTypeDefinitionProvider>();
    }
}
