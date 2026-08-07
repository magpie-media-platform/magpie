using System;

using Magpie.Extensions.Music.Subjects;
using Magpie.Sdk.Content;
using Magpie.Sdk.Extensions;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.Music;

/// <summary>
/// The Magpie extension for the Music content type.
/// </summary>
public sealed class MusicExtension : IExtensionLifecycle
{
    private static readonly Guid ExtensionId = new("37c92dfb-37bc-4de4-8e68-e7f9615b378f");

    /// <inheritdoc />
    public Guid Id => ExtensionId;

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public string Name => "Music";

    /// <inheritdoc />
    public string? TargetABI => "[1.0.0,)";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IContentTypeDefinitionProvider, MusicContentTypeDefinitionProvider>();
        services.AddSingleton<ISubjectKindDefinitionProvider, LabelSubjectKindDefinitionProvider>();
    }
}
