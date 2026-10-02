using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary.QueryEngine;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

namespace Magpie.Extensions.OpenLibrary;

internal sealed class OpenLibraryMisPairedSubjectDiagnostic(
    ISubjectService subjects,
    IMetadataService metadata) : IDiagnosticCheck
{
    public const string Id = "openlibrary.content.mis-paired-subject";

    private const string AffectedEntityPrefix = "Subject:";

    private const int PageSize = 500;

    public string DiagnosticCheckId => Id;

    public string Category => "Metadata";

    internal static string AffectedEntity(Guid subjectId) => AffectedEntityPrefix + subjectId.ToString("D");

    internal static bool TryParseAffectedEntity(string? affectedEntity, out Guid subjectId)
    {
        subjectId = Guid.Empty;
        return affectedEntity is not null &&
            affectedEntity.StartsWith(AffectedEntityPrefix, StringComparison.Ordinal) &&
            Guid.TryParse(affectedEntity.Substring(AffectedEntityPrefix.Length), out subjectId);
    }

    internal static bool IsMisPaired(string name, string key) =>
        key.NormalizeOlid('A') is null &&
        !string.Equals(key, name.ToOpenLibrarySubjectKey(), StringComparison.Ordinal) &&
        !string.Equals(key, name.NormalizeSubjectKey(), StringComparison.Ordinal);

    internal static (string Name, string Key, bool HasPrimaryImage)? ReadOpenLibrarySubject(
        IReadOnlyList<MetadataContribution> contributions)
    {
        var contribution = contributions.FirstOrDefault(candidate => string.Equals(
            candidate.ProviderType,
            Constants.ProviderType,
            StringComparison.Ordinal));
        var key = contribution?.ExternalIds
            .FirstOrDefault(externalId => string.Equals(
                externalId.Source,
                Constants.ProviderType,
                StringComparison.Ordinal))?
            .Value;
        return contribution?.Title is { Length: > 0 } name && key is { Length: > 0 }
            ? (name, key, contributions.Any(candidate => candidate.Images.Any(image => image.IsPrimary)))
            : null;
    }

    internal static async Task<HashSet<Guid>> ListUserEditedIdsAsync(
        ISubjectService subjects,
        IReadOnlyCollection<Guid>? ids,
        CancellationToken cancellationToken)
    {
        var withUserContributions = await ListAsync(
            subjects,
            CandidateQuery(ids, hasUserContributions: true, hasTags: null),
            cancellationToken).ConfigureAwait(false);
        var withTags = await ListAsync(
            subjects,
            CandidateQuery(ids, hasUserContributions: null, hasTags: true),
            cancellationToken).ConfigureAwait(false);
        return [.. withUserContributions.Concat(withTags).Select(subject => subject.Id)];
    }

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        var edited = await ListUserEditedIdsAsync(subjects, null, cancellationToken).ConfigureAwait(false);
        var candidates = await ListAsync(
            subjects,
            CandidateQuery(null, hasUserContributions: null, hasTags: null),
            cancellationToken).ConfigureAwait(false);

        var paired = new List<(Subject Subject, string Name, string Key)>();
        foreach (var subject in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contributions = await metadata.GetSubjectContributionsAsync(subject.Id, cancellationToken)
                .ConfigureAwait(false);
            if (contributions is not null && ReadOpenLibrarySubject(contributions) is { } read)
            {
                paired.Add((subject, read.Name, read.Key));
                if (read.HasPrimaryImage)
                {
                    edited.Add(subject.Id);
                }
            }
        }

        var findings = new List<DiagnosticFinding>();
        var misPaired = new HashSet<Guid>();
        foreach (var (subject, name, key) in paired.Where(pair => IsMisPaired(pair.Name, pair.Key)))
        {
            misPaired.Add(subject.Id);
            var isEdited = edited.Contains(subject.Id);
            findings.Add(new DiagnosticFinding
            {
                DiagnosticCheckId = Id,
                Severity = DiagnosticFindingSeverity.Warning,
                Title = $"{name} has a mis-paired Open Library key",
                Detail = $"The {subject.Kind} subject {name} holds the Open Library key {key}, which does not " +
                    "match its name." + (isEdited ? " The subject has user edits." : string.Empty),
                AffectedEntity = AffectedEntity(subject.Id),
                RepairId = isEdited ? null : Constants.MisPairedSubjectRepairId,
            });
        }

        var duplicateGroups = paired
            .Where(pair => !misPaired.Contains(pair.Subject.Id))
            .GroupBy(pair => (pair.Subject.Kind, NameKey: pair.Name.ToOpenLibrarySubjectKey()))
            .Where(group => group.Count() > 1);
        foreach (var group in duplicateGroups)
        {
            foreach (var (subject, name, key) in group.Where(pair => edited.Contains(pair.Subject.Id)))
            {
                var others = group
                    .Where(other => other.Subject.Id != subject.Id)
                    .Select(other => $"{other.Name} ({other.Key})")
                    .ToList();
                findings.Add(new DiagnosticFinding
                {
                    DiagnosticCheckId = Id,
                    Severity = DiagnosticFindingSeverity.Warning,
                    Title = $"{name} is a duplicate subject",
                    Detail = $"The {subject.Kind} subject {name} ({key}) has user edits, and " +
                        $"{string.Join(", ", others)} {(others.Count == 1 ? "has" : "have")} the same name.",
                    AffectedEntity = AffectedEntity(subject.Id),
                });
            }
        }

        return findings;
    }

    private static SubjectQuery CandidateQuery(
        IReadOnlyCollection<Guid>? ids,
        bool? hasUserContributions,
        bool? hasTags) =>
        new()
        {
            Ids = ids,
            Kinds = [Constants.PersonSubjectKind, Constants.PlaceSubjectKind, Constants.TimeSubjectKind],
            ExternalId = new()
            {
                Source = new()
                {
                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = Constants.ProviderType }]
                }
            },
            HasUserContributions = hasUserContributions,
            HasTags = hasTags,
        };

    private static async Task<List<Subject>> ListAsync(
        ISubjectService subjects,
        SubjectQuery query,
        CancellationToken cancellationToken)
    {
        var result = new List<Subject>();
        for (var pageNumber = 1; ; pageNumber++)
        {
            var page = await subjects
                .QueryAsync(query, new() { PageNumber = pageNumber, PageSize = PageSize }, null, cancellationToken)
                .ConfigureAwait(false);
            var items = page.Items.ToList();
            result.AddRange(items);
            if (items.Count == 0 || pageNumber * PageSize >= page.TotalItems)
            {
                return result;
            }
        }
    }
}
