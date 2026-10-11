using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

namespace Magpie.Data;

/// <summary>
/// The options builder extensions that add the bundled data seeding to <see cref="MagpieDbContext"/>.
/// </summary>
public static class MagpieSeedingExtensions
{
    private static readonly Guid SystemUserId = new("11111111-1111-1111-1111-111111111111");

    private static readonly Guid NuGetPackageSourceId = new("5d9a2b5f-5c59-4a1f-8c70-8011a8b4e0b1");

    private static readonly Guid AnyQualityTierListId = new("5e467568-37b5-4cb9-a272-80f508d86db8");

    private static readonly Guid AnyQualityTierId = new("bbfcfb85-70dd-481c-815b-d5973e2a22a8");

    private static readonly Guid VideoHdTierListId = new("6f73ee55-eaa7-4a35-a3bf-f9bf075f4659");

    private static readonly Guid Video4KTierListId = new("1636e9e9-7436-40c3-a58a-d6887f016e65");

    private static readonly Guid VideoSpaceTierListId = new("72304ea0-9dfe-49ae-8d77-42e0fe3825e4");

    private static readonly Guid AudioBalancedTierListId = new("463c0781-7dab-4373-a3ff-faf9e8f0dd6a");

    private static readonly Guid AudioLosslessTierListId = new("d2072059-be78-4b5e-aa6c-f5abe1afb5b8");

    private static readonly Guid AudioCompactTierListId = new("1364faf0-5e33-41cf-b305-ed6f7a5add9c");

    private static readonly Guid AudioSpokenTierListId = new("8e951e4f-cdce-490b-9c2e-b7b9cde37e49");

    private static readonly DateTime QualityCatalogSeededAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string Resolution2160 =
        @"(?<![a-z0-9])(?:2160p|uhd)(?![a-z0-9])|(?:\[|\()4k(?![a-z0-9])";

    private const string Resolution1080 = @"(?<![a-z0-9])1080p(?![a-z0-9])";
    private const string Resolution720 = @"(?<![a-z0-9])720p(?![a-z0-9])";
    private const string Resolution480 = @"(?<![a-z0-9])480p(?![a-z0-9])";
    private const string SourceRemux = @"(?<![a-z0-9])remux(?![a-z0-9])";
    private const string SourceBluRay = @"(?<![a-z0-9])blu-?ray(?![a-z0-9])";
    private const string SourceWebDl = @"(?<![a-z0-9])web-?dl(?![a-z0-9])";
    private const string SourceHdtv = @"(?<![a-z0-9])hdtv(?![a-z0-9])";
    private const string SourceLowQuality = @"(?<![a-z0-9])(hdcam|telesync|telecine|hdts)(?![a-z0-9])";

    private const string SourceLowQualityOrRemux =
        @"(?<![a-z0-9])(hdcam|telesync|telecine|hdts|remux)(?![a-z0-9])";

    private const string CodecHevc = @"(?<![a-z0-9])(hevc|h[.]?265|x265)(?![a-z0-9])";
    private const string CodecAv1 = @"(?<![a-z0-9])av1(?![a-z0-9])";

    private const string SourceLossyTranscode =
        @"(?:\[|\()(?:transcode|transcoded)(?![a-z0-9])|(?<![a-z0-9])lossy-source(?![a-z0-9])";

    private const string FormatLossless =
        @"(?<![a-z0-9])(?:flac|alac|wav|aiff)(?![a-z0-9])|" +
        @"(?:\[|\()(?:wave|ape|lossless|wv)(?![a-z0-9])|[.](?:wave|ape|wv)$";

    private const string FormatFlacAlac = @"(?<![a-z0-9])(flac|alac)(?![a-z0-9])";

    private const string FormatWavApe =
        @"(?<![a-z0-9])wav(?![a-z0-9])|(?:\[|\()(?:wave|ape)(?![a-z0-9])|[.](?:wave|ape)$";

    private const string FormatAiffWavPack =
        @"(?<![a-z0-9])(?:aiff|wavpack)(?![a-z0-9])|(?:\[|\()wv(?![a-z0-9])|[.]wv$";

    private const string FormatMp3 = @"(?<![a-z0-9])(mp3|cbr)(?![a-z0-9])";
    private const string FormatOpus = @"(?:\[|\()opus(?![a-z0-9])|[.]opus$";
    private const string FormatAacM4A = @"(?<![a-z0-9])(aac|m4a)(?![a-z0-9])";
    private const string FormatOpusAacM4A = FormatAacM4A + "|" + FormatOpus;

    private const string FormatLossy =
        @"(?<![a-z0-9])(?:mp3|aac|ogg|vorbis)(?![a-z0-9])|" + FormatOpus;

    private const string FormatM4B = @"(?<![a-z0-9])m4b(?![a-z0-9])";
    private const string BitDepth24 = @"(?<![a-z0-9])24[- .]?bit(?![a-z0-9])";
    private const string Bitrate320 = @"(?<![a-z0-9])320(?:k(?:bps|b/s|bit/s)?)?(?![a-z0-9])";
    private const string BitrateV0 = @"(?<![a-z0-9])v0(?![a-z0-9])";
    private const string BitrateV2 = @"(?<![a-z0-9])v2(?![a-z0-9])";
    private const string Bitrate256 = @"(?<![a-z0-9])256(?:k(?:bps|b/s|bit/s)?)?(?![a-z0-9])";
    private const string Bitrate192 = @"(?<![a-z0-9])192(?:k(?:bps|b/s|bit/s)?)?(?![a-z0-9])";
    private const string Bitrate128 = @"(?<![a-z0-9])128(?:k(?:bps|b/s|bit/s)?)?(?![a-z0-9])";
    private const string Bitrate96 = @"(?<![a-z0-9])96(?:k(?:bps|b/s|bit/s)?)?(?![a-z0-9])";
    private const string Bitrate64 = @"(?<![a-z0-9])64(?:k(?:bps|b/s|bit/s)?)?(?![a-z0-9])";
    private const string Multichannel = @"(?<![a-z0-9])(?:[3-9][.]0|[2-9][.]1)(?![a-z0-9]|[.][0-9])";

    /// <summary>
    /// Registers the idempotent bundled-data seeding for the database.
    /// </summary>
    /// <param name="optionsBuilder">The options builder to configure</param>
    /// <param name="scheduleTriggers">The default schedule triggers to seed</param>
    /// <param name="eventTriggers">The default event triggers to seed</param>
    /// <returns>The same options builder for chaining</returns>
    public static DbContextOptionsBuilder UseMagpieSeeding(
        this DbContextOptionsBuilder optionsBuilder,
        IReadOnlyList<ScheduleTrigger> scheduleTriggers,
        IReadOnlyList<EventTrigger> eventTriggers)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(scheduleTriggers);
        ArgumentNullException.ThrowIfNull(eventTriggers);

        return optionsBuilder
            .UseSeeding((context, _) =>
                context.SeedAsync(scheduleTriggers, eventTriggers, default).GetAwaiter().GetResult())
            .UseAsyncSeeding((context, _, cancellationToken) =>
                context.SeedAsync(scheduleTriggers, eventTriggers, cancellationToken));
    }

    private static async Task SeedAsync(
        this DbContext context,
        IReadOnlyList<ScheduleTrigger> scheduleTriggers,
        IReadOnlyList<EventTrigger> eventTriggers,
        CancellationToken cancellationToken)
    {
        var firstSeed = !await context.Set<User>()
            .AnyAsync(user => user.Id == SystemUserId, cancellationToken)
            .ConfigureAwait(false);
        if (firstSeed)
        {
            context.Set<User>().Add(new()
            {
                Id = SystemUserId,
                Username = "system",
                ProviderType = "system",
                Enabled = false,
                Role = Role.Admin,
                CreatedAt = DateTime.UnixEpoch
            });
        }

        var now = DateTime.UtcNow;

        if (!await context.Set<ScheduleTrigger>().AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            context.Set<ScheduleTrigger>().AddRange(scheduleTriggers);
        }

        await context.Set<ScheduleTrigger>()
            .Where(trigger => trigger.JobType == "PruneOrphanedSubjects")
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(trigger => trigger.JobType, "SubjectMaintenance")
                    .SetProperty(
                        trigger => trigger.Name,
                        trigger => trigger.Name == "Orphaned subject pruning schedule"
                            ? "Subject maintenance schedule"
                            : trigger.Name),
                cancellationToken)
            .ConfigureAwait(false);

        await context.Set<DiagnosticFinding>()
            .Where(finding => finding.DiagnosticCheckId == "core.content.duplicate-subject")
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await context.Set<EventTrigger>().AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            context.Set<EventTrigger>().AddRange(eventTriggers);
        }

        if (!await context.Set<MimeMapping>().AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            var curated = new MimeMapping[]
            {
                new() { Extension = ".ass", MimeType = "text/x-ssa", CreatedAt = now },
                new() { Extension = ".avi", MimeType = "video/x-msvideo", CreatedAt = now },
                new() { Extension = ".azw", MimeType = "application/vnd.amazon.ebook", CreatedAt = now },
                new() { Extension = ".azw3", MimeType = "application/vnd.amazon.mobi8-ebook", CreatedAt = now },
                new() { Extension = ".epub", MimeType = "application/epub+zip", CreatedAt = now },
                new() { Extension = ".flac", MimeType = "audio/flac", CreatedAt = now },
                new() { Extension = ".m4a", MimeType = "audio/mp4", CreatedAt = now },
                new() { Extension = ".m4b", MimeType = "audio/mp4", CreatedAt = now },
                new() { Extension = ".mkv", MimeType = "video/x-matroska", CreatedAt = now },
                new() { Extension = ".mobi", MimeType = "application/x-mobipocket-ebook", CreatedAt = now },
                new() { Extension = ".mov", MimeType = "video/quicktime", CreatedAt = now },
                new() { Extension = ".mp3", MimeType = "audio/mpeg", CreatedAt = now },
                new() { Extension = ".mp4", MimeType = "video/mp4", CreatedAt = now },
                new() { Extension = ".ogg", MimeType = "audio/ogg", CreatedAt = now },
                new() { Extension = ".ogv", MimeType = "video/ogg", CreatedAt = now },
                new() { Extension = ".opf", MimeType = "application/oebps-package+xml", CreatedAt = now },
                new() { Extension = ".opus", MimeType = "audio/opus", CreatedAt = now },
                new() { Extension = ".srt", MimeType = "application/x-subrip", CreatedAt = now },
                new() { Extension = ".ssa", MimeType = "text/x-ssa", CreatedAt = now },
                new() { Extension = ".sub", MimeType = "text/x-microdvd", CreatedAt = now },
                new() { Extension = ".vtt", MimeType = "text/vtt", CreatedAt = now },
                new() { Extension = ".wav", MimeType = "audio/wav", CreatedAt = now },
            };
            context.Set<MimeMapping>().AddRange(curated);

            var seeded = new HashSet<string>(
                curated.Select(mapping => mapping.Extension),
                StringComparer.OrdinalIgnoreCase);
            foreach (var (extension, mimeType) in new FileExtensionContentTypeProvider().Mappings)
            {
                var normalized = extension.ToLowerInvariant();
                if (!seeded.Add(normalized))
                {
                    continue;
                }

                context.Set<MimeMapping>().Add(new()
                {
                    Extension = normalized,
                    MimeType = mimeType,
                    CreatedAt = now,
                });
            }
        }

        if (!await context.Set<BlockedFileType>().AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            context.Set<BlockedFileType>().AddRange(
                new() { Id = Guid.NewGuid(), Pattern = ".bat", CreatedAt = now },
                new() { Id = Guid.NewGuid(), Pattern = ".cmd", CreatedAt = now },
                new() { Id = Guid.NewGuid(), Pattern = ".com", CreatedAt = now },
                new() { Id = Guid.NewGuid(), Pattern = ".dll", CreatedAt = now },
                new() { Id = Guid.NewGuid(), Pattern = ".dylib", CreatedAt = now },
                new() { Id = Guid.NewGuid(), Pattern = ".exe", CreatedAt = now },
                new() { Id = Guid.NewGuid(), Pattern = ".js", CreatedAt = now },
                new() { Id = Guid.NewGuid(), Pattern = ".jse", CreatedAt = now },
                new() { Id = Guid.NewGuid(), Pattern = ".msi", CreatedAt = now },
                new() { Id = Guid.NewGuid(), Pattern = ".pif", CreatedAt = now },
                new() { Id = Guid.NewGuid(), Pattern = ".ps1", CreatedAt = now },
                new() { Id = Guid.NewGuid(), Pattern = ".scr", CreatedAt = now },
                new() { Id = Guid.NewGuid(), Pattern = ".so", CreatedAt = now },
                new() { Id = Guid.NewGuid(), Pattern = ".vbe", CreatedAt = now },
                new() { Id = Guid.NewGuid(), Pattern = ".vbs", CreatedAt = now },
                new() { Id = Guid.NewGuid(), Pattern = ".wsf", CreatedAt = now });
        }

        if (!await context.Set<ExtensionPackageSource>()
            .AnyAsync(source => source.Id == NuGetPackageSourceId, cancellationToken).ConfigureAwait(false))
        {
            context.Set<ExtensionPackageSource>().Add(new()
            {
                Id = NuGetPackageSourceId,
                Name = "nuget.org",
                SourceUrl = "https://api.nuget.org/v3/index.json",
                Enabled = true,
                SearchFilterKind = ExtensionPackageSourceSearchFilterKind.Tag,
                SearchFilterValue = "magpie",
                CredentialKind = ExtensionPackageSourceCredentialKind.None,
                CreatedAt = DateTime.UnixEpoch
            });
        }

        if (!await context.Set<ExtensionActivationState>()
            .AnyAsync(state => state.Id == 1, cancellationToken).ConfigureAwait(false))
        {
            context.Set<ExtensionActivationState>().Add(new() { Id = 1, Generation = 0 });
        }

        if (firstSeed)
        {
            await SeedQualityTierCatalogAsync(context, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SeedQualityTierCatalogAsync(
        DbContext context,
        CancellationToken cancellationToken)
    {
        var catalog = BuildQualityTierCatalog();
        var catalogTierIds = catalog
            .SelectMany(list => list.QualityTierRanks)
            .Select(rank => rank.QualityTierId)
            .Distinct()
            .ToArray();
        var existingTierIds = await context.Set<QualityTier>()
            .Where(tier => catalogTierIds.Contains(tier.Id))
            .Select(tier => tier.Id)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);
        existingTierIds.UnionWith(context.Set<QualityTier>().Local.Select(tier => tier.Id));

        foreach (var list in catalog)
        {
            var ranks = list.QualityTierRanks.ToArray();
            list.QualityTierRanks.Clear();
            context.Set<QualityTierList>().Add(list);

            foreach (var rank in ranks)
            {
                rank.QualityTierListId = list.Id;
                var tier = rank.QualityTier ??
                    throw new InvalidOperationException("The quality tier catalog rank has no quality tier.");
                rank.QualityTier = null;
                if (existingTierIds.Add(tier.Id))
                {
                    context.Set<QualityTier>().Add(tier);
                }

                context.Set<QualityTierListRank>().Add(rank);
            }
        }
    }

    private static IReadOnlyList<QualityTierList> BuildQualityTierCatalog() =>
    [
        new()
        {
            Id = AnyQualityTierListId,
            Name = "Any",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UnixEpoch,
            QualityTierRanks =
            [
                new()
                {
                    QualityTierId = AnyQualityTierId,
                    QualityTier = new()
                    {
                        Id = AnyQualityTierId,
                        Name = "Any",
                        Criteria = new(),
                        CreatedAt = DateTime.UnixEpoch
                    },
                    Ordinal = 0,
                    IsCutoff = true,
                    CreatedAt = DateTime.UnixEpoch
                }
            ]
        },
        TierList(
            VideoHdTierListId,
            "Video - HD",
            "video/*",
            new()
            {
                Id = new("8a86ca19-9073-4f32-aea1-6a0f274bc5eb"),
                Name = "Low-quality captures",
                Kind = RuleKind.Exclude,
                Criteria = Match(SourceLowQuality)
            },
            [
                Rank(
                    new("083107aa-3d53-40fb-8c04-f30168bf4b6c"),
                    "1080p Remux",
                    ordinal: 0,
                    isCutoff: false,
                    Match(Resolution1080, SourceRemux)),
                Rank(
                    new("21493027-98c8-47fc-83a3-9d799851f637"),
                    "1080p BluRay",
                    ordinal: 1,
                    isCutoff: true,
                    Match(Resolution1080, SourceBluRay)),
                Rank(
                    new("a1f312b5-80a2-4e6e-8987-fc4807d2805f"),
                    "1080p WEB-DL",
                    ordinal: 2,
                    isCutoff: false,
                    Match(Resolution1080, SourceWebDl)),
                Rank(
                    new("89f7f521-6bf5-4391-aed8-1e93697e828f"),
                    "1080p HDTV",
                    ordinal: 3,
                    isCutoff: false,
                    Match(Resolution1080, SourceHdtv)),
                Rank(
                    new("c3a162f3-9395-458a-8566-faff6f6d08b9"),
                    "1080p",
                    ordinal: 4,
                    isCutoff: false,
                    Match(Resolution1080)),
                Rank(
                    new("4de4039e-fdb6-4401-b0f3-8c2edd15fdc9"),
                    "720p",
                    ordinal: 5,
                    isCutoff: false,
                    Match(Resolution720)),
                Rank(
                    new("690049d0-664c-4c17-b6d2-e45f5da33abb"),
                    "480p",
                    ordinal: 6,
                    isCutoff: false,
                    Match(Resolution480))
            ]),
        TierList(
            Video4KTierListId,
            "Video - 4K",
            "video/*",
            new()
            {
                Id = new("166842a4-2799-4456-a3a3-1f5f6f36a473"),
                Name = "Low-quality captures",
                Kind = RuleKind.Exclude,
                Criteria = Match(SourceLowQuality)
            },
            [
                Rank(
                    new("df2b71de-2403-4582-8ee5-0b291a825492"),
                    "2160p Remux",
                    ordinal: 0,
                    isCutoff: false,
                    Match(Resolution2160, SourceRemux)),
                Rank(
                    new("755885ae-d08c-43d4-a29d-c5d4cf13f845"),
                    "2160p BluRay",
                    ordinal: 1,
                    isCutoff: true,
                    Match(Resolution2160, SourceBluRay)),
                Rank(
                    new("366349f1-a9fa-4c63-8c01-940d0b4bdffe"),
                    "2160p WEB-DL",
                    ordinal: 2,
                    isCutoff: false,
                    Match(Resolution2160, SourceWebDl)),
                Rank(
                    new("5fda62dd-0ed9-4eaa-bb86-3de35f834b46"),
                    "2160p",
                    ordinal: 3,
                    isCutoff: false,
                    Match(Resolution2160))
            ]),
        TierList(
            VideoSpaceTierListId,
            "Video - Space Saver",
            "video/*",
            new()
            {
                Id = new("73f8b944-be4a-48cf-8af2-cacdb1b5c0ad"),
                Name = "Large or low-quality sources",
                Kind = RuleKind.Exclude,
                Criteria = Match(SourceLowQualityOrRemux)
            },
            [
                Rank(
                    new("eaa85ab8-e644-45a2-8b9c-b473303d10b1"),
                    "1080p AV1",
                    ordinal: 0,
                    isCutoff: false,
                    Match(Resolution1080, CodecAv1)),
                Rank(
                    new("2618efaa-6fea-444e-8442-92a7fb5ceec3"),
                    "1080p HEVC",
                    ordinal: 1,
                    isCutoff: true,
                    Match(Resolution1080, CodecHevc)),
                Rank(
                    new("91dba129-5794-48d3-b972-0a0a6f6760c0"),
                    "720p HEVC",
                    ordinal: 2,
                    isCutoff: false,
                    Match(Resolution720, CodecHevc)),
                Rank(
                    new("acb33649-71c1-4765-a0e5-3c5c38a11681"),
                    "720p",
                    ordinal: 3,
                    isCutoff: false,
                    Match(Resolution720)),
                Rank(
                    new("294a978e-5ea9-4b1f-934d-b6d6c6f630e5"),
                    "480p",
                    ordinal: 4,
                    isCutoff: false,
                    Match(Resolution480))
            ]),
        TierList(
            AudioBalancedTierListId,
            "Audio - Balanced",
            "audio/*",
            new()
            {
                Id = new("744ca2fd-3f02-4596-977b-8ba01d5484da"),
                Name = "Lossy transcodes",
                Kind = RuleKind.Exclude,
                Criteria = Match(SourceLossyTranscode)
            },
            [
                Rank(
                    new("9ee483e1-e27a-4924-906e-e51b389ef485"),
                    "Lossless",
                    ordinal: 0,
                    isCutoff: false,
                    Match(FormatLossless)),
                Rank(
                    new("7c4afa52-5135-4cbd-a6a3-60cb1478d889"),
                    "MP3 320 kbps",
                    ordinal: 1,
                    isCutoff: false,
                    Match(FormatMp3, Bitrate320)),
                Rank(
                    new("bb2ae6c9-729b-4591-abc2-19ae2287bea2"),
                    "MP3 V0",
                    ordinal: 2,
                    isCutoff: true,
                    Match(FormatMp3, BitrateV0)),
                Rank(
                    new("979eee78-ebc6-456f-826d-001cfabe506b"),
                    "MP3 V2",
                    ordinal: 3,
                    isCutoff: false,
                    Match(FormatMp3, BitrateV2)),
                Rank(
                    new("cf994124-f8fd-40a8-98fe-f3a076022e96"),
                    "256 kbps",
                    ordinal: 4,
                    isCutoff: false,
                    Match(FormatMp3, Bitrate256)),
                Rank(
                    new("8a1b88e2-f528-448c-b723-0def6a4e5c67"),
                    "192 kbps",
                    ordinal: 5,
                    isCutoff: false,
                    Match(FormatMp3, Bitrate192)),
                Rank(
                    new("cd189bc0-b604-4a95-9ab1-39eaaeeae03a"),
                    "128 kbps",
                    ordinal: 6,
                    isCutoff: false,
                    Match(FormatMp3, Bitrate128))
            ]),
        TierList(
            AudioLosslessTierListId,
            "Audio - Lossless",
            "audio/*",
            new()
            {
                Id = new("755e230c-0b60-4758-b08f-dcae084317c4"),
                Name = "Lossy formats",
                Kind = RuleKind.Exclude,
                Criteria = Match(FormatLossy)
            },
            [
                Rank(
                    new("04ebbec0-4cda-41f9-a442-c682beaa2f39"),
                    "24-bit FLAC/ALAC",
                    ordinal: 0,
                    isCutoff: false,
                    Match(FormatFlacAlac, BitDepth24)),
                Rank(
                    new("7628f40b-b80a-4a83-86e0-be3c7081a344"),
                    "FLAC/ALAC",
                    ordinal: 1,
                    isCutoff: true,
                    Match(FormatFlacAlac)),
                Rank(
                    new("708c47ef-9476-4c9d-8679-e46271be9cec"),
                    "WAV/APE",
                    ordinal: 2,
                    isCutoff: false,
                    Match(FormatWavApe)),
                Rank(
                    new("8188c24c-2334-4de8-82b3-7433d81fbf41"),
                    "AIFF/WavPack",
                    ordinal: 3,
                    isCutoff: false,
                    Match(FormatAiffWavPack))
            ]),
        TierList(
            AudioCompactTierListId,
            "Audio - Compact",
            "audio/*",
            new()
            {
                Id = new("ea8d910c-7859-48dd-abfe-73ebb3475ebb"),
                Name = "Lossless formats",
                Kind = RuleKind.Exclude,
                Criteria = Match(FormatLossless)
            },
            [
                Rank(
                    new("8b018a63-94f1-40d6-8070-1506768d86e0"),
                    "Opus 128 kbps",
                    ordinal: 0,
                    isCutoff: false,
                    Match(FormatOpus, Bitrate128)),
                Rank(
                    new("f2be00f5-8f30-4847-9417-4ec8eeb77db5"),
                    "AAC 128 kbps",
                    ordinal: 1,
                    isCutoff: true,
                    Match(FormatAacM4A, Bitrate128)),
                Rank(
                    new("bb3161aa-8e2a-4f0c-8a4c-c9d7e4487091"),
                    "MP3 128 kbps",
                    ordinal: 2,
                    isCutoff: false,
                    Match(FormatMp3, Bitrate128)),
                Rank(
                    new("b526ab33-1fa3-4033-89d2-eb74cc8ed761"),
                    "Opus/AAC 96 kbps",
                    ordinal: 3,
                    isCutoff: false,
                    Match(FormatOpusAacM4A, Bitrate96))
            ]),
        TierList(
            AudioSpokenTierListId,
            "Audio - Spoken Word",
            "audio/*",
            new()
            {
                Id = new("493ca6d8-f8e2-4b88-920a-9e13380d167f"),
                Name = "Multichannel audio",
                Kind = RuleKind.Exclude,
                Criteria = Match(Multichannel)
            },
            [
                Rank(
                    new("c05c9039-d96f-439c-82c0-c2f74ff929a2"),
                    "M4B",
                    ordinal: 0,
                    isCutoff: false,
                    Match(FormatM4B)),
                Rank(
                    new("cd081cbe-494d-4283-9203-645b2f4e497c"),
                    "Opus 96 kbps",
                    ordinal: 1,
                    isCutoff: true,
                    Match(FormatOpus, Bitrate96)),
                Rank(
                    new("1e52db51-f4f5-40f7-9c56-4594d16774d2"),
                    "AAC/MP3 96 kbps",
                    ordinal: 2,
                    isCutoff: false,
                    Match(@"(?<![a-z0-9])(aac|m4a|mp3)(?![a-z0-9])", Bitrate96)),
                Rank(
                    new("b0d31078-5ede-4970-b51d-fba5c6c39abf"),
                    "AAC/MP3 64 kbps",
                    ordinal: 3,
                    isCutoff: false,
                    Match(@"(?<![a-z0-9])(aac|m4a|mp3)(?![a-z0-9])", Bitrate64))
            ])
    ];

    private static QualityTierList TierList(
        Guid id,
        string name,
        string mimeType,
        QualityTierRule exclusion,
        IReadOnlyList<QualityTierListRank> ranks) => new()
        {
            Id = id,
            Name = name,
            MimeTypes = [mimeType],
            UpgradeAllowed = true,
            Rules = [exclusion],
            CreatedAt = QualityCatalogSeededAt,
            QualityTierRanks = [.. ranks]
        };

    private static QualityTierListRank Rank(
        Guid tierId,
        string name,
        int ordinal,
        bool isCutoff,
        QualityTierCriteria criteria) => new()
        {
            QualityTierId = tierId,
            Ordinal = ordinal,
            IsCutoff = isCutoff,
            CreatedAt = QualityCatalogSeededAt,
            QualityTier = new()
            {
                Id = tierId,
                Name = name,
                Criteria = criteria,
                CreatedAt = QualityCatalogSeededAt
            }
        };

    private static QualityTierCriteria Match(params string[] titlePatterns) => new()
    {
        And = titlePatterns.Select(pattern => new DictionaryCriteria<PropertyCriterion<string>>
        {
            Items = new(StringComparer.Ordinal)
            {
                ["Title"] = new()
                {
                    Operator = CriteriaValueOperator.Regex,
                    Value = pattern
                }
            }
        }).ToList()
    };
}
