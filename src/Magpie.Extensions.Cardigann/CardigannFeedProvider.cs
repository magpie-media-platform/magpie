using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Cardigann.Engine;
using Magpie.Extensions.Cardigann.Git;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Integrations;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Cardigann;

internal sealed class CardigannFeedProvider(
    IGitDefinitionSource definitionSource,
    ICardigannLoader loader,
    ILogger<CardigannFeedProvider> logger) : IIndexerFeedProvider
{
    private const string TokenSettingKey = "token";

    public string ProviderType => Constants.ProviderTypeName;

    public IReadOnlyList<SettingSchemaDefinition> SettingsSchema
    {
        get
        {
            return
            [
                new()
                {
                    Key = Constants.GitUrlSettingKey,
                    Type = DataType.String,
                    DisplayName = "Git URL",
                    Description =
                        "Full HTTPS git URL to read definitions from (GitHub, GitLab, Gitea, Forgejo, self-hosted).",
                    DefaultValue = Constants.DefaultGitUrl,
                    Placeholder = Constants.DefaultGitUrl,
                },
                new()
                {
                    Key = Constants.BranchSettingKey,
                    Type = DataType.String,
                    DisplayName = "Branch",
                    Description = "The git branch to read definitions from.",
                    DefaultValue = Constants.DefaultBranch,
                    Placeholder = Constants.DefaultBranch,
                },
                new()
                {
                    Key = Constants.DefinitionsPathSettingKey,
                    Type = DataType.String,
                    DisplayName = "Definitions path",
                    Description = "Path inside the repository that holds the *.yml definition files.",
                    DefaultValue = Constants.DefaultDefinitionsPath,
                    Placeholder = Constants.DefaultDefinitionsPath,
                },
                new()
                {
                    Key = TokenSettingKey,
                    Type = DataType.String,
                    DisplayName = "Access token",
                    Description = "Optional access token for a private remote. Sent as the git password.",
                    Secret = true,
                },
            ];
        }
    }

    private static Indexer BuildIndexer(CardigannDefinition definition, string rawYaml)
    {
        var categoryMappings = definition.ResolveCategoryMappings();
        var categories = categoryMappings
            .Select(m => m.Cat)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var availability = definition.Type?.ToLowerInvariant() switch
        {
            "public" => IndexerAvailability.Public,
            "private" => IndexerAvailability.Private,
            "semi-private" => IndexerAvailability.SemiPrivate,
            _ => IndexerAvailability.Unknown,
        };

        return new Indexer
        {
            ProviderType = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            DefinitionType = Constants.ProviderTypeName,
            SchemaData = rawYaml,
            Availability = availability,
            Categories = categories,
            Languages = string.IsNullOrWhiteSpace(definition.Language) ? [] : [definition.Language!],
            SupportedContentTypes = [],
        };
    }

    public async Task<IEnumerable<Indexer>> SyncAsync(IndexerFeed feed, CancellationToken cancellationToken = default)
    {
        var settings = ReadSettings(feed);
        var files = await definitionSource.LoadDefinitionsAsync(
            settings.GitUrl,
            settings.Branch,
            settings.DefinitionsPath,
            settings.Token,
            cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Cardigann feed {Feed} found {Count} definition file(s) in {Url}@{Branch}/{Path}",
            feed.Name,
            files.Count,
            settings.GitUrl,
            settings.Branch,
            settings.DefinitionsPath);

        var indexers = new List<Indexer>(files.Count);
        foreach (var (name, yaml) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var indexer = ParseDefinition(name, yaml);
            if (indexer is not null)
            {
                indexers.Add(indexer);
            }
        }

        logger.LogInformation(
            "Cardigann feed {Feed} parsed {Parsed} of {Total} definitions and skipped {Skipped} unsupported files",
            feed.Name,
            indexers.Count,
            files.Count,
            files.Count - indexers.Count);
        return indexers;
    }

    public async Task<bool> TestConnectionAsync(IndexerFeed feed, CancellationToken cancellationToken = default)
    {
        var settings = ReadSettings(feed);
        try
        {
            var files = await definitionSource.LoadDefinitionsAsync(
                settings.GitUrl,
                settings.Branch,
                settings.DefinitionsPath,
                settings.Token,
                cancellationToken)
                .ConfigureAwait(false);
            return files.Count > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cardigann feed test failed for {Url}@{Branch}", settings.GitUrl, settings.Branch);
            return false;
        }
    }

    private static (string GitUrl, string Branch, string DefinitionsPath, string? Token) ReadSettings(IndexerFeed feed)
    {
        string Read(string key, string fallback)
        {
            return feed.Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : fallback;
        }

        var branch = Read(Constants.BranchSettingKey, Constants.DefaultBranch);
        var definitionsPath = Read(Constants.DefinitionsPathSettingKey, Constants.DefaultDefinitionsPath).Trim('/');
        var token = Read(TokenSettingKey, string.Empty);
        var gitUrl = Read(Constants.GitUrlSettingKey, Constants.DefaultGitUrl);

        return (gitUrl, branch, definitionsPath, token.Length == 0 ? null : token);
    }

    private Indexer? ParseDefinition(string fileName, string yaml)
    {
        CardigannDefinition definition;
        try
        {
            definition = loader.LoadString(yaml, fileName);
        }
        catch (CardigannDefinitionException)
        {
            return null;
        }

        return BuildIndexer(definition, yaml);
    }
}
