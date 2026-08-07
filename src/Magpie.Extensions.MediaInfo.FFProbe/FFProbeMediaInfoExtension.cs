using System;

using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Media;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.MediaInfo.FFProbe;

/// <summary>
/// The Magpie extension for FFprobe media info.
/// </summary>
public sealed class FFProbeMediaInfoExtension : IExtension
{
    private static readonly Guid ExtensionId = new("F4C1D6B2-7A0E-4F0A-9D2B-15B8A2F19D01");

    /// <inheritdoc />
    public Guid Id => ExtensionId;

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public string Name => "MediaInfo (FFprobe)";

    /// <inheritdoc />
    public string? TargetABI => "[1.0.0,)";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddSingleton<IMediaInfoExtractor, FFProbeMediaInfoExtractor>();

        services.AddScoped<IDiagnosticCheck, FFProbeBinaryMissingDiagnostic>();
    }
}
