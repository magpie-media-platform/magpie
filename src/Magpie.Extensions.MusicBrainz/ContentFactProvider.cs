using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Models.Releases;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

namespace Magpie.Extensions.MusicBrainz;

internal sealed class ContentFactProvider(IMetadataService metadataService) : IContentFactProvider
{
    public bool CanProvideFacts(ContentTarget target) =>
        string.Equals(target.Item.ContentType, Constants.ContentTypeSlug, StringComparison.Ordinal);

    public async Task<IReadOnlyList<IReadOnlyList<ContentFact>>> GetFactsAsync(
        ContentTarget target,
        CancellationToken cancellationToken = default)
    {
        ContentHierarchyItem? artist = null;
        ContentHierarchyItem? album = null;
        ContentHierarchyItem? track = null;
        foreach (var node in target.Lineage)
        {
            if (string.Equals(node.Level, Constants.ArtistLevelSlug, StringComparison.Ordinal))
            {
                artist = node;
            }
            else if (string.Equals(node.Level, Constants.AlbumLevelSlug, StringComparison.Ordinal))
            {
                album = node;
            }
            else if (string.Equals(node.Level, Constants.TrackLevelSlug, StringComparison.Ordinal))
            {
                track = node;
            }
        }

        if (track is null && album is null)
        {
            return [];
        }

        var focus = track ?? album!;
        var artists = new List<string>();
        var relatedMetadata = await metadataService
            .GetRelatedMetadataAsync(focus.Id, [Constants.ArtistRelationshipKind], cancellationToken)
            .ConfigureAwait(false);
        foreach (var metadata in relatedMetadata)
        {
            var alternateTitles =
                from alternate in metadata.AlternateTexts ?? []
                where (string.Equals(alternate.Kind, "Name", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(alternate.Kind, "Title", StringComparison.OrdinalIgnoreCase)) &&
                    alternate.Text.Split([' '], StringSplitOptions.RemoveEmptyEntries).Length > 1
                select alternate.Text;
            artists.AddRange(
                from title in alternateTitles.Prepend(metadata.OriginalTitle).Prepend(metadata.Title)
                where !string.IsNullOrWhiteSpace(title)
                select title.Trim());
        }

        if (artists.Count == 0 && artist is not null)
        {
            var fallback = target.Metadata.TryGetValue(artist.Id, out var artistMetadata)
                ? artistMetadata.Title
                : null;
            if (fallback is not null)
            {
                artists.Add(fallback);
            }
        }

        var albumTitle = album is not null && target.Metadata.TryGetValue(album.Id, out var albumMetadata)
            ? albumMetadata.Title
            : null;
        var trackTitle = track is not null && target.Metadata.TryGetValue(track.Id, out var trackMetadata)
            ? trackMetadata.Title
            : null;
        var rows = new List<(string? Artist, string? Album, string? Track)>();
        if (track is not null)
        {
            if (trackTitle is null)
            {
                return [];
            }

            foreach (var artistTitle in artists)
            {
                if (albumTitle is not null)
                {
                    rows.Add((artistTitle, albumTitle, trackTitle));
                }

                rows.Add((artistTitle, null, trackTitle));
            }
        }
        else if (albumTitle is not null)
        {
            foreach (var artistTitle in artists)
            {
                rows.Add((artistTitle, albumTitle, null));
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var factSets = new List<IReadOnlyList<ContentFact>>();
        foreach (var row in rows)
        {
            var facts = new List<ContentFact>();
            if (row.Artist is not null)
            {
                facts.Add(new ContentFact { Value = row.Artist });
            }

            if (row.Album is not null)
            {
                facts.Add(new ContentFact { Value = row.Album });
            }

            if (row.Track is not null)
            {
                facts.Add(new ContentFact { Value = row.Track });
            }

            if (facts.Count == 0)
            {
                continue;
            }

            var key = string.Join("\u001f", facts.Select(fact => fact.Value ?? string.Empty));
            if (seen.Add(key))
            {
                factSets.Add(facts);
            }
        }

        return factSets;
    }
}
