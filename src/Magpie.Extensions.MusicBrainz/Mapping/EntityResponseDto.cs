namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record EntityResponseDto
{
    public ArtistDto? Artist { get; init; }

    public ReleaseGroupDto? ReleaseGroup { get; init; }

    public ReleaseDto? Release { get; init; }

    public RecordingDto? Recording { get; init; }

    public LabelDto? Label { get; init; }
}
