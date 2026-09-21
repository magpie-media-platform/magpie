using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using GitCore;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Cardigann.Git;

internal sealed class GitCoreDefinitionSource(
    IHttpClientFactory httpClientFactory,
    ILogger<GitCoreDefinitionSource> logger) : IGitDefinitionSource
{
    public const string HttpClientName = "cardigann-gitcore";

    private static string NormalizeGitUrl(string remoteUrl)
    {
        var trimmed = remoteUrl.Trim().TrimEnd('/');
        return trimmed.EndsWith(".git", StringComparison.Ordinal) ? trimmed : trimmed + ".git";
    }

    private static (string BaseUrl, string Owner, string Repo) SplitGitUrl(string gitUrl)
    {
        var withoutSuffix = gitUrl[..^".git".Length];
        var lastSlash = withoutSuffix.LastIndexOf('/');
        if (lastSlash <= 0)
        {
            throw new ArgumentException($"Git URL {gitUrl} has no repository path.", nameof(gitUrl));
        }

        var repo = withoutSuffix[(lastSlash + 1)..];
        var beforeRepo = withoutSuffix[..lastSlash];
        var ownerSlash = beforeRepo.LastIndexOf('/');
        if (ownerSlash <= 0)
        {
            throw new ArgumentException($"Git URL {gitUrl} has no owner segment.", nameof(gitUrl));
        }

        var owner = beforeRepo[(ownerSlash + 1)..];
        var baseUrl = beforeRepo[..ownerSlash];
        return (baseUrl, owner, repo);
    }

    private static IReadOnlyList<string> SplitPath(string definitionsPath)
    {
        return [.. definitionsPath.Split(['/'], StringSplitOptions.RemoveEmptyEntries)];
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadDefinitionsAsync(
        string remoteUrl,
        string branch,
        string definitionsPath,
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            throw new ArgumentException("Remote URL is required.", nameof(remoteUrl));
        }

        if (string.IsNullOrWhiteSpace(branch))
        {
            throw new ArgumentException("Branch is required.", nameof(branch));
        }

        var gitUrl = NormalizeGitUrl(remoteUrl);
        var (baseUrl, owner, repo) = SplitGitUrl(gitUrl);

        using var httpClient = CreateHttpClient(token);
        using var cancellation = cancellationToken.Register(httpClient.CancelPendingRequests);

        cancellationToken.ThrowIfCancellationRequested();
        var commitSha = await GitSmartHttp
            .FetchBranchCommitShaAsync(baseUrl, owner, repo, branch, httpClient)
            .ConfigureAwait(false);

        var subdirectory = SplitPath(definitionsPath);
        cancellationToken.ThrowIfCancellationRequested();
        var contents = await LoadFromUrl
            .LoadSubdirectoryContentsFromGitUrlAsync(gitUrl, commitSha, subdirectory, httpClient)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var definitions = new Dictionary<string, string>();
        foreach (var (path, bytes) in contents)
        {
            if (path.Count != 1)
            {
                continue;
            }

            var name = path[0];
            if (!name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            definitions[name] = Encoding.UTF8.GetString(bytes.Span);
        }

        logger.LogDebug(
            "Cardigann GitCore loaded {Count} definition(s) from {Url}@{Branch}/{Path} (sha {Sha})",
            definitions.Count,
            gitUrl,
            branch,
            definitionsPath,
            commitSha);
        return definitions;
    }

    private HttpClient CreateHttpClient(string? token)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        if (!string.IsNullOrWhiteSpace(token))
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"git:{token}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        return client;
    }
}
