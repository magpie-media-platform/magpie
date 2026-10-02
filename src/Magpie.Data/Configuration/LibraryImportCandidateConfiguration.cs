using System.Collections.Generic;
using System.Text.Json;

using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class LibraryImportCandidateConfiguration : IEntityTypeConfiguration<LibraryImportCandidate>
{
    public void Configure(EntityTypeBuilder<LibraryImportCandidate> builder)
    {
        builder.ToTable("content_libraryImportCandidate");
        builder.Property(e => e.Matches).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<List<LibraryImportMatch>>(v, JsonSerializerOptions.Default)
                ?? new())
            .UseListComparer();
        builder.Property(e => e.ParsedSignals).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => string.IsNullOrWhiteSpace(v)
                ? new()
                : JsonSerializer.Deserialize<Dictionary<string, List<string>>>(v, JsonSerializerOptions.Default)
                    ?? new())
            .UseJsonComparer();
        builder.Property(e => e.MatchCount).HasDefaultValue(0);
        builder.HasIndex(e => e.GroupKey);
        builder.HasOne<RootFolder>()
            .WithMany()
            .HasForeignKey(e => e.RootFolderId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.RootFolderId, e.ScanRunId });
        builder.HasIndex(e => new { e.Status, e.MatchCount });

        builder.HasIndex(e => e.ContentHierarchyItemId);
    }
}
