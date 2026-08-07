using System;

namespace Magpie.Extensions.MusicBrainz;

internal static class Constants
{
    internal static readonly Guid ExtensionId = new("95171BBE-D8F6-4771-9434-860AC2B5BC30");

    public const string ProviderType = "musicbrainz";

    public const string ContentTypeSlug = "music";

    public const string ArtistLevelSlug = "artist";

    public const string AlbumLevelSlug = "album";

    public const string TrackLevelSlug = "track";

    public const string IfpiSource = "ifpi";

    public const string LabelSubjectKind = "music-label";

    public const string CoverImageType = "cover";

    public const string ArtistRelationshipKind = "artist";

    public const string BandMembershipRelationshipKind = "member of band";

    public const string PerformerRelationshipKind = "performer";

    public const string CrewRelationshipKind = "crew";

    public const string WriterRelationshipKind = "writer";

    public const string PersonSubjectKind = "person";

    public const string PersonArtistType = "Person";

    public const string ArtistRelationTarget = "artist";

    public const string WorkRelationTarget = "work";

    public const string InstrumentRelationshipType = "instrument";

    public const string VocalRelationshipType = "vocal";

    public const string PerformerRelationshipType = "performer";

    public const string PerformingOrchestraRelationshipType = "performing orchestra";

    public const string ProducerRelationshipType = "producer";

    public const string EngineerRelationshipType = "engineer";

    public const string AudioRelationshipType = "audio";

    public const string AudioEngineerRelationshipType = "audio engineer";

    public const string SoundRelationshipType = "sound";

    public const string SoundEngineerRelationshipType = "sound engineer";

    public const string RecordingRelationshipType = "recording";

    public const string RecordingEngineerRelationshipType = "recording engineer";

    public const string FieldRecordistRelationshipType = "field recordist";

    public const string MasteringRelationshipType = "mastering";

    public const string MasteringEngineerRelationshipType = "mastering engineer";

    public const string BalanceRelationshipType = "balance";

    public const string MixRelationshipType = "mix";

    public const string MixerRelationshipType = "mixer";

    public const string ArrangerRelationshipType = "arranger";

    public const string InstrumentArrangerRelationshipType = "instrument arranger";

    public const string VocalArrangerRelationshipType = "vocal arranger";

    public const string OrchestratorRelationshipType = "orchestrator";

    public const string ComposerRelationshipType = "composer";

    public const string LyricistRelationshipType = "lyricist";

    public const string WriterRelationshipType = "writer";

    public const string ProducerRole = "Producer";

    public const string EngineerRole = "Engineer";

    public const string MixerRole = "Mixer";

    public const string ArrangerRole = "Arranger";

    public const string ComposerRole = "Composer";

    public const string LyricistRole = "Lyricist";

    public const string WriterRole = "Writer";

    public const string VocalsRole = "Vocals";

    public const string WorkSubjectKind = "work";

    public const string AlternateTextKind = "name";

    public const string IsrcSource = "isrc";

    public const string IsniSource = "isni";

    public const string IpiSource = "ipi";
}
