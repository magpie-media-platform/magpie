using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata;

internal static class Constants
{
    public const string ProviderType = "wikidata";

    public const string MovieContentType = "movie";

    public const string TvContentType = "tv";

    public const string MovieLevel = "movie";

    public const string ShowLevel = "show";

    public const string SeasonLevel = "season";

    public const string EpisodeLevel = "episode";

    public const string ImdbSource = "imdb";

    public const string TmdbSource = "tmdb";

    public const string TvdbSource = "tvdb";

    public const string ImdbPropertyId = "P345";

    public const string TmdbMoviePropertyId = "P4947";

    public const string TmdbShowPropertyId = "P4983";

    public const string TmdbSeasonPropertyId = "P12558";

    public const string TmdbEpisodePropertyId = "P12559";

    public const string TmdbPersonPropertyId = "P4985";

    public const string TmdbCompanyPropertyId = "P11806";

    public const string TvdbShowPropertyId = "P4835";

    public const string TvdbSeasonPropertyId = "P12397";

    public const string TvdbEpisodePropertyId = "P7043";

    public const string GenrePropertyId = "P136";

    public const string PublicationDatePropertyId = "P577";

    public const string SeriesStartPropertyId = "P580";

    public const string SeriesEndPropertyId = "P582";

    public const string InstanceOfPropertyId = "P31";

    public const string OriginalTitlePropertyId = "P1476";

    public const string OriginalLanguagePropertyId = "P364";

    public const string LanguageCodePropertyId = "P218";

    public const string DurationPropertyId = "P2047";

    public const string ImagePropertyId = "P18";

    public const string PosterPropertyId = "P3383";

    public const string LogoPropertyId = "P154";

    public const string PartOfSeriesPropertyId = "P179";

    public const string SeasonPropertyId = "P4908";

    public const string SeriesOrdinalPropertyId = "P1545";

    public const string ProductionCodePropertyId = "P2364";

    public const string OfficialNamePropertyId = "P1448";

    public const string InceptionPropertyId = "P571";

    public const string DissolutionPropertyId = "P576";

    public const string OfficialWebsitePropertyId = "P856";

    public const string TrailerPropertyId = "P1651";

    public const string BirthDatePropertyId = "P569";

    public const string DeathDatePropertyId = "P570";

    public const string AwardReceivedPropertyId = "P166";

    public const string PointInTimePropertyId = "P585";

    public const string ConferredByPropertyId = "P1027";

    public const string MpaaRatingPropertyId = "P1657";

    public const string FskRatingPropertyId = "P1981";

    public const string BbfcRatingPropertyId = "P2629";

    public const string CncRatingPropertyId = "P2758";

    public const string EirinRatingPropertyId = "P2756";

    public const string IncaaRatingPropertyId = "P3428";

    public const string KmrbRatingPropertyId = "P3818";

    public const string CastMemberPropertyId = "P161";

    public const string VoiceActorPropertyId = "P725";

    public const string DirectorPropertyId = "P57";

    public const string ScreenwriterPropertyId = "P58";

    public const string ProducerPropertyId = "P162";

    public const string CinematographerPropertyId = "P344";

    public const string FilmEditorPropertyId = "P1040";

    public const string ComposerPropertyId = "P86";

    public const string ExecutiveProducerPropertyId = "P1431";

    public const string ProductionDesignerPropertyId = "P2554";

    public const string CostumeDesignerPropertyId = "P2515";

    public const string NarratorPropertyId = "P2438";

    public const string PresenterPropertyId = "P371";

    public const string ProductionCompanyPropertyId = "P272";

    public const string OriginalBroadcasterPropertyId = "P449";

    public const string DistributorPropertyId = "P750";

    public const string CharacterRoleNamePropertyId = "P4633";

    public const string CharacterRolePropertyId = "P453";

    public const string MovieQid = "Q11424";

    public const string ShortFilmQid = "Q24862";

    public const string TelevisionFilmQid = "Q506240";

    public const string TelevisionSeriesQid = "Q5398426";

    public const string AnimatedSeriesQid = "Q581714";

    public const string SeasonQid = "Q3464665";

    public const string EpisodeQid = "Q21191270";

    public const string HumanQid = "Q5";

    public const string SecondUnitQid = "Q11574";

    public const string MinuteUnitQid = "Q7727";

    public const string HourUnitQid = "Q25235";

    public const string EndedProductionStatus = "Ended";

    public const string WonAwardStatus = "Won";

    public static readonly IReadOnlyList<(string PropertyId, string Body)> RatingSystems =
    [
        (MpaaRatingPropertyId, "MPAA"),
        (BbfcRatingPropertyId, "BBFC"),
        (FskRatingPropertyId, "FSK"),
        (CncRatingPropertyId, "CNC"),
        (EirinRatingPropertyId, "EIRIN"),
        (IncaaRatingPropertyId, "INCAA"),
        (KmrbRatingPropertyId, "KMRB")
    ];
}
