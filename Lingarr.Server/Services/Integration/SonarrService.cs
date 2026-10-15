using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Integrations;

namespace Lingarr.Server.Services.Integration;

public class SonarrService : ISonarrService
{
    private readonly IIntegrationService _integrationService;

    public SonarrService(IIntegrationService integrationService)
    {
        _integrationService = integrationService;
    }
    
    /// <inheritdoc />
    public async Task<List<SonarrShow>?> GetShows()
    {
        var shows = await _integrationService.GetApiResponse<List<SonarrShow>>(
            "/api/v3/series",
            new IntegrationSettingKeys
            {
                Url = "sonarr_url",
                ApiKey = "sonarr_api_key"
            });
        if (shows == null) return null;

        foreach (var show in shows)
        {
            await FillMissingSeasons(show);
        }

        return shows;
    }

    /// <inheritdoc />
    public async Task<SonarrShow?> GetShow(int showId)
    {
        var show = await _integrationService.GetApiResponse<SonarrShow>(
            $"/api/v3/series/{showId}",
            new IntegrationSettingKeys
            {
                Url = "sonarr_url",
                ApiKey = "sonarr_api_key"
            });
        if (show != null)
        {
            await FillMissingSeasons(show);
        }

        return show;
    }
    
    /// <inheritdoc />
    public async Task<List<SonarrEpisode>?> GetEpisodes(int seriesNumber, int seasonNumber)
    {
        var episodes = await _integrationService.GetApiResponse<List<SonarrEpisode>>(
            $"/api/v3/episode?seriesId={seriesNumber}&seasonNumber={seasonNumber}&includeImages=true",
            new IntegrationSettingKeys
            {
                Url = "sonarr_url",
                ApiKey = "sonarr_api_key"
            });

        // Sonarr-compatible APIs such as nexcrate ignore seasonNumber and return the whole series
        return episodes?.Where(episode => episode.SeasonNumber == seasonNumber).ToList();
    }

    /// <inheritdoc />
    public async Task<SonarrEpisode?> GetEpisode(long episodeNumber)
    {
        return await _integrationService.GetApiResponse<SonarrEpisode>(
            $"/api/v3/episode/{episodeNumber}?includeImages=true",
            new IntegrationSettingKeys
            {
                Url = "sonarr_url",
                ApiKey = "sonarr_api_key"
            });
    }
    
    /// <inheritdoc />
    public async Task<SonarrEpisodePath?> GetEpisodePath(long episodeId)
    {
        return await _integrationService.GetApiResponse<SonarrEpisodePath>(
            $"/api/v3/episode/{episodeId}",
            new IntegrationSettingKeys
            {
                Url = "sonarr_url",
                ApiKey = "sonarr_api_key"
            });
    }

    /// <summary>
    /// Derives the seasons from the episode list when the API does not include them, as nexcrate does
    /// </summary>
    private async Task FillMissingSeasons(SonarrShow show)
    {
        if (show.Seasons.Count > 0) return;

        var episodes = await _integrationService.GetApiResponse<List<SonarrEpisode>>(
            $"/api/v3/episode?seriesId={show.Id}",
            new IntegrationSettingKeys
            {
                Url = "sonarr_url",
                ApiKey = "sonarr_api_key"
            });
        if (episodes == null) return;

        show.Seasons = episodes
            .Select(episode => episode.SeasonNumber)
            .Distinct()
            .OrderBy(seasonNumber => seasonNumber)
            .Select(seasonNumber => new SonarrSeason { SeasonNumber = seasonNumber })
            .ToList();
    }
}
