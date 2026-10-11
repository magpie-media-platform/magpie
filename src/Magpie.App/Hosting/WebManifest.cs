using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Magpie.App.Hosting;

internal static class WebManifest
{
    public const string RequestPath = "/manifest.webmanifest";

    private const string FileName = "manifest.webmanifest";
    private const string ContentType = "application/manifest+json";

    public static async Task<IResult> GetAsync(
        IWebHostEnvironment environment,
        ISettingsService settingsService,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var file = environment.WebRootFileProvider.GetFileInfo(FileName);
        if (!file.Exists)
        {
            return Results.NotFound();
        }

        var instanceName = await settingsService.GetInstanceNameAsync(cancellationToken).ConfigureAwait(false);

        var stream = file.CreateReadStream();
        await using (stream.ConfigureAwait(false))
        {
            var manifest = (await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false))!.AsObject();
            manifest["name"] = instanceName;
            manifest["short_name"] = instanceName;

            response.Headers.CacheControl = CacheControlHeaderValue.NoCacheString;

            return Results.Content(manifest.ToJsonString(), ContentType);
        }
    }
}
