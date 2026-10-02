using System;

using Magpie.Sdk.Content;
using Magpie.Sdk.Extensions;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.Tv;

/// <summary>
/// The Magpie extension for the TV content type.
/// </summary>
public sealed class TvExtension : IExtensionLifecycle
{
    private static readonly Guid ExtensionId = new("E0A22EC5-9073-4E50-A7C9-F02B787A107C");

    /// <inheritdoc />
    public Guid Id => ExtensionId;

    /// <inheritdoc />
    public string Name => "TV";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IContentTypeDefinitionProvider, TvContentTypeDefinitionProvider>();
        services.AddSingleton<IContentFactProvider, ContentFactProvider>();
    }
}
