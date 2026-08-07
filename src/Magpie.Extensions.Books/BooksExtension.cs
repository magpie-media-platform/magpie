using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Books.Subjects;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Sdk.Content;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.Books;

/// <summary>
/// The Magpie extension for the Book content type.
/// </summary>
public sealed class BooksExtension : IExtensionLifecycle
{
    private static readonly Guid ExtensionId = new("8ed67ec9-fc9a-4d09-a5f1-5d27701975aa");
    private static readonly Guid RetailTierListId = new("5afb7256-c6e5-4550-b97f-7980a2835a32");
    private static readonly Guid CompatibilityTierListId = new("5df23dff-c7c2-46de-a0a1-c8e3c5e73019");
    private static readonly Guid ArchiveTierListId = new("22076a35-1223-466b-a8d1-4b30c620ca2e");
    private static readonly Guid TierRetailReflowable = new("6a60b3e3-9c22-49f7-88dd-cbdc1ccca338");
    private static readonly Guid TierRetailMobi = new("7f0e1f68-c66e-42f2-af52-b8a47e8d5631");
    private static readonly Guid TierWebConverted = new("c3089392-ebd8-4d44-a24c-b453b790cef6");
    private static readonly Guid TierReflowable = new("14297965-aeb4-43fb-8d58-d2574708408c");
    private static readonly Guid TierPdf = new("8d599785-24bb-44b6-bd28-166e8998cda0");
    private static readonly Guid TierCompatibilityEpub = new("9df6251f-17a0-4f3d-a7ee-fc926b4ee82b");
    private static readonly Guid TierCompatibilityAzw = new("2cb6a470-8103-4dd0-aa0f-8b347f2fbb1d");
    private static readonly Guid TierCompatibilityMobi = new("6eaf8a93-6313-4831-9236-1f65136f4c0c");
    private static readonly Guid TierCompatibilityPdf = new("9efd2c5e-4711-4d48-b8bb-7ebf96967c63");
    private static readonly Guid TierArchiveScan = new("2d5516c9-36ed-4ffb-8559-d8ad4e7b7178");
    private static readonly Guid TierArchiveOcr = new("1d6ba87f-e5dd-421b-95b6-4172c961e1cc");
    private static readonly Guid TierArchivePdf = new("b9b59b6b-6488-4b5e-b991-b70e09a77c93");
    private static readonly Guid RetailExclusionId = new("2b695387-dc6b-4f16-bf60-9350168cba4d");
    private static readonly Guid CompatibilityExclusionId = new("68c8d2f7-e015-4393-9d50-1027106e2fd5");
    private static readonly Guid ArchiveExclusionId = new("fef4e9d0-0f54-44a2-9014-3907d2c956ed");
    private static readonly DateTime SeededAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string SourceSampleOrPreview =
        @"(?:\[|\()(?:sample|preview)(?![a-z0-9])|" +
        @"[. ](?:sample|preview)(?=[. ]+(?:\[|\()?(?:epub|azw3|azw|mobi|pdf)(?![a-z0-9]))";

    private const string SourceRetail =
        @"(?:\[|\()retail(?![a-z0-9])|" +
        @"[. ]retail(?=[. ]+(?:\[|\()?(?:epub|azw3|azw|mobi)(?![a-z0-9]))";

    private const string SourceWebOrConverted =
        @"(?:\[|\()(?:web|converted)(?![a-z0-9])|" +
        @"[. ](?:web|converted)(?=[. ]+(?:\[|\()?(?:epub|azw3|azw)(?![a-z0-9]))";

    private const string SourceScan =
        @"(?:\[|\()(?:scan|scanned)(?![a-z0-9])|" +
        @"[. ](?:scan|scanned)(?=[. ]+(?:\[|\()?pdf(?![a-z0-9]))";

    private const string SourceOcr = @"(?<![a-z0-9])ocr(?![a-z0-9])";
    private const string FormatReflowable = @"(?<![a-z0-9])(epub|azw3|azw)(?![a-z0-9])";
    private const string FormatEpub = @"(?<![a-z0-9])epub(?![a-z0-9])";
    private const string FormatAzw = @"(?<![a-z0-9])(azw3|azw)(?![a-z0-9])";
    private const string FormatMobi = @"(?<![a-z0-9])mobi(?![a-z0-9])";
    private const string FormatReflowableOrMobi = @"(?<![a-z0-9])(epub|azw3|azw|mobi)(?![a-z0-9])";
    private const string FormatPdf = @"(?<![a-z0-9])pdf(?![a-z0-9])";

    /// <inheritdoc />
    public Guid Id => ExtensionId;

    /// <inheritdoc />
    public Version Version => new(1, 0, 1);

    /// <inheritdoc />
    public string Name => "Books";

    /// <inheritdoc />
    public string? TargetABI => "[1.0.0,)";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IContentTypeDefinitionProvider, BooksContentTypeDefinitionProvider>();
        services.AddSingleton<ISubjectKindDefinitionProvider, PublisherSubjectKindDefinitionProvider>();
    }

    /// <inheritdoc />
    public async Task OnExtensionInstallAsync(
        ExtensionLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var qualityService = context.ServiceProvider.GetRequiredService<IQualityService>();
        await EnsureStarterQualityTierListsAsync(qualityService, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OnExtensionUpgradeAsync(
        ExtensionLifecycleContext context,
        Version previousVersion,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var qualityService = context.ServiceProvider.GetRequiredService<IQualityService>();
        await EnsureStarterQualityTierListsAsync(qualityService, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureStarterQualityTierListsAsync(
        IQualityService qualityService,
        CancellationToken cancellationToken)
    {
        foreach (var tierList in BuildStarterQualityTierLists())
        {
            if (await qualityService.GetTierListAsync(tierList.Id, cancellationToken)
                .ConfigureAwait(false) is not null)
            {
                continue;
            }

            foreach (var rank in tierList.QualityTierRanks)
            {
                if (await qualityService.GetTierAsync(rank.QualityTierId, cancellationToken)
                    .ConfigureAwait(false) is not null)
                {
                    rank.QualityTier = null;
                }
            }

            await qualityService.CreateTierListAsync(tierList, actingUserId: null, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<QualityTierList> BuildStarterQualityTierLists() =>
    [
        TierList(
            RetailTierListId,
            "E-book - Retail and Reflowable",
            Exclude(
                RetailExclusionId,
                "Samples and previews",
                Match(SourceSampleOrPreview)),
            [
                Rank(
                    RetailTierListId,
                    TierRetailReflowable,
                    "Retail EPUB/AZW3",
                    ordinal: 0,
                    isCutoff: false,
                    Match(SourceRetail, FormatReflowable)),
                Rank(
                    RetailTierListId,
                    TierRetailMobi,
                    "Retail MOBI",
                    ordinal: 1,
                    isCutoff: false,
                    Match(SourceRetail, FormatMobi)),
                Rank(
                    RetailTierListId,
                    TierWebConverted,
                    "Web/Converted EPUB",
                    ordinal: 2,
                    isCutoff: true,
                    Match(SourceWebOrConverted, FormatReflowable)),
                Rank(
                    RetailTierListId,
                    TierReflowable,
                    "EPUB/AZW3/MOBI",
                    ordinal: 3,
                    isCutoff: false,
                    Match(FormatReflowableOrMobi)),
                Rank(
                    RetailTierListId,
                    TierPdf,
                    "PDF",
                    ordinal: 4,
                    isCutoff: false,
                    Match(FormatPdf))
            ]),
        TierList(
            CompatibilityTierListId,
            "E-book - Broad Compatibility",
            Exclude(
                CompatibilityExclusionId,
                "Samples and previews",
                Match(SourceSampleOrPreview)),
            [
                Rank(
                    CompatibilityTierListId,
                    TierCompatibilityEpub,
                    "EPUB",
                    ordinal: 0,
                    isCutoff: true,
                    Match(FormatEpub)),
                Rank(
                    CompatibilityTierListId,
                    TierCompatibilityAzw,
                    "AZW/AZW3",
                    ordinal: 1,
                    isCutoff: false,
                    Match(FormatAzw)),
                Rank(
                    CompatibilityTierListId,
                    TierCompatibilityMobi,
                    "MOBI",
                    ordinal: 2,
                    isCutoff: false,
                    Match(FormatMobi)),
                Rank(
                    CompatibilityTierListId,
                    TierCompatibilityPdf,
                    "PDF",
                    ordinal: 3,
                    isCutoff: false,
                    Match(FormatPdf))
            ]),
        TierList(
            ArchiveTierListId,
            "E-book - Archive and Scans",
            Exclude(
                ArchiveExclusionId,
                "Samples and previews",
                Match(SourceSampleOrPreview)),
            [
                Rank(
                    ArchiveTierListId,
                    TierArchiveScan,
                    "Scanned PDF",
                    ordinal: 0,
                    isCutoff: false,
                    Match(FormatPdf, SourceScan)),
                Rank(
                    ArchiveTierListId,
                    TierArchiveOcr,
                    "OCR PDF",
                    ordinal: 1,
                    isCutoff: true,
                    Match(FormatPdf, SourceOcr)),
                Rank(
                    ArchiveTierListId,
                    TierArchivePdf,
                    "PDF",
                    ordinal: 2,
                    isCutoff: false,
                    Match(FormatPdf))
            ])
    ];

    private static QualityTierList TierList(
        Guid id,
        string name,
        QualityTierRule exclusion,
        IReadOnlyList<QualityTierListRank> ranks) => new()
        {
            Id = id,
            Name = name,
            MimeTypes =
            [
                "application/epub+zip",
                "application/pdf",
                "application/vnd.amazon.ebook",
                "application/x-mobipocket-ebook"
            ],
            UpgradeAllowed = true,
            Rules = [exclusion],
            CreatedAt = SeededAt,
            QualityTierRanks = [.. ranks]
        };

    private static QualityTierListRank Rank(
        Guid listId,
        Guid tierId,
        string name,
        int ordinal,
        bool isCutoff,
        QualityTierCriteria criteria) => new()
        {
            QualityTierListId = listId,
            QualityTierId = tierId,
            Ordinal = ordinal,
            IsCutoff = isCutoff,
            CreatedAt = SeededAt,
            QualityTier = new()
            {
                Id = tierId,
                Name = name,
                CreatedAt = SeededAt,
                Criteria = criteria
            }
        };

    private static QualityTierRule Exclude(Guid id, string name, QualityTierCriteria criteria) => new()
    {
        Id = id,
        Name = name,
        Kind = RuleKind.Exclude,
        Criteria = criteria
    };

    private static QualityTierCriteria Match(params string[] titlePatterns) => new()
    {
        And = titlePatterns.Select(pattern => new DictionaryCriteria<PropertyCriterion<string>>
        {
            Items = new Dictionary<string, PropertyCriterion<string>>(StringComparer.Ordinal)
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
