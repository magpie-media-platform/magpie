using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tvmaze.Mapping;
using Magpie.Extensions.Tvmaze.Pipeline;

namespace Magpie.Extensions.Tvmaze;

internal interface IClient
{
    IDisposable? BeginExecutionTrace(Action<WireAttemptOutcome> recorder) => null;

    Task<IReadOnlyList<SearchHitDto>> SearchShowsAsync(string query, CancellationToken cancellationToken);

    Task<IReadOnlyList<ShowDto>?> GetShowsPageAsync(int page, CancellationToken cancellationToken);

    Task<IReadOnlyList<PersonSearchHitDto>> SearchPeopleAsync(string query, CancellationToken cancellationToken);

    Task<PersonDto?> GetPersonByIdAsync(long personId, CancellationToken cancellationToken);

    Task<ShowDto?> GetShowByIdAsync(int showId, CancellationToken cancellationToken);

    Task<ShowDto?> GetShowAsync(int showId, CancellationToken cancellationToken);

    Task<ShowDto?> GetShowWithCreditsAsync(int showId, CancellationToken cancellationToken);

    Task<ShowDto?> GetShowWithEmbedsAsync(
        int showId,
        IReadOnlyList<string> embeds,
        CancellationToken cancellationToken);

    Task<EpisodeDto?> GetEpisodeAsync(long episodeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SeasonDto>> GetSeasonsAsync(int showId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CastCreditDto>> GetPersonCastCreditsAsync(long personId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CrewCreditDto>> GetPersonCrewCreditsAsync(long personId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GuestCastEntryDto>> GetEpisodeGuestCastAsync(
        long episodeId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GuestCrewEntryDto>> GetEpisodeGuestCrewAsync(
        long episodeId,
        CancellationToken cancellationToken);

    Task<ShowDto?> LookupShowAsync(string source, string externalId, CancellationToken cancellationToken);
}
