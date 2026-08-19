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
            artists.AddRange(GetTitles(metadata));
        }

        artists = artists.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (artists.Count == 0 && artist is not null)
        {
            if (target.Metadata.TryGetValue(artist.Id, out var artistMetadata))
            {
                artists.AddRange(GetTitles(artistMetadata));
            }
        }

        var albumTitles = album is not null && target.Metadata.TryGetValue(album.Id, out var albumMetadata)
            ? GetTitles(albumMetadata)
            : [];
        var trackTitles = track is not null && target.Metadata.TryGetValue(track.Id, out var trackMetadata)
            ? GetTitles(trackMetadata)
            : [];
        var rows = new List<(string? Artist, string? Album, string? Track)>();
        if (track is not null)
        {
            if (trackTitles.Count == 0)
            {
                return [];
            }

            foreach (var artistTitle in artists)
            {
                foreach (var trackTitle in trackTitles)
                {
                    foreach (var albumTitle in albumTitles)
                    {
                        rows.Add((artistTitle, albumTitle, trackTitle));
                    }

                    rows.Add((artistTitle, null, trackTitle));
                }
            }
        }
        else
        {
            foreach (var artistTitle in artists)
            {
                foreach (var albumTitle in albumTitles)
                {
                    rows.Add((artistTitle, albumTitle, null));
                }
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

    private static List<string> GetTitles(MaterializedMetadata metadata) =>
        new[] { metadata.Title, metadata.OriginalTitle }
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => title!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
