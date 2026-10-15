using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Integrations;
using Lingarr.Server.Services.Integration;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Integration;

/// <summary>
/// nexcrate answers below /bazarr/radarr and /bazarr/sonarr with a subset of Radarr's and Sonarr's API.
/// The payloads below follow its shapes.
/// </summary>
public class NexcrateCompatibilityTests
{
    private const long NexcrateEpisodeId = 42L * 100_000_000 + 7;

    [Fact]
    public void SonarrShow_WithoutSeasonFolderSeasonsOrAdded_Deserializes()
    {
        const string json = """
            [{"id": 42, "title": "Show", "path": "/media/tv/Show", "rootFolderPath": "/media/tv",
              "images": [{"coverType": "poster", "url": "/MediaCover/42/poster.jpg", "remoteUrl": null}],
              "added": null}]
            """;

        var shows = JsonSerializer.Deserialize<List<SonarrShow>>(json);

        var show = Assert.Single(shows!);
        Assert.Equal(42, show.Id);
        Assert.Null(show.Added);
        Assert.Empty(show.Seasons);
    }

    [Fact]
    public void SonarrEpisode_WithPartialSeriesAndLargeId_Deserializes()
    {
        var json = $$$"""
            {"id": {{{NexcrateEpisodeId}}}, "seasonNumber": 1, "episodeNumber": 2, "title": "Pilot", "hasFile": true,
             "series": {"id": 42, "title": "Show", "year": 2020}}
            """;

        var episode = JsonSerializer.Deserialize<SonarrEpisode>(json);

        Assert.Equal(NexcrateEpisodeId, episode!.Id);
        Assert.Equal(42, episode.Show!.Id);
        Assert.Empty(episode.Show.Path);
    }

    [Fact]
    public void RadarrMovie_WithNullAdded_Deserializes()
    {
        const string json = """
            {"id": 5, "title": "Movie", "path": "/media/movies/Movie", "rootFolderPath": "/media/movies",
             "added": null, "hasFile": true, "movieFile": {"path": "/media/movies/Movie/Movie.mkv"}}
            """;

        var movie = JsonSerializer.Deserialize<RadarrMovie>(json);

        Assert.Null(movie!.Added);
        Assert.Equal("/media/movies/Movie/Movie.mkv", movie.MovieFile.Path);
    }

    [Fact]
    public async Task GetEpisodes_FiltersBySeason_WhenApiIgnoresSeasonNumber()
    {
        var integration = new Mock<IIntegrationService>();
        integration
            .Setup(i => i.GetApiResponse<List<SonarrEpisode>>(It.IsAny<string>(), It.IsAny<IntegrationSettingKeys>()))
            .ReturnsAsync(new List<SonarrEpisode> { Episode(1, 1), Episode(2, 1), Episode(3, 2) });

        var episodes = await new SonarrService(integration.Object).GetEpisodes(42, 1);

        Assert.Equal(new long[] { 1, 2 }, episodes!.Select(e => e.Id));
    }

    [Fact]
    public async Task GetShows_DerivesSeasonsFromEpisodes_WhenMissing()
    {
        var integration = new Mock<IIntegrationService>();
        integration
            .Setup(i => i.GetApiResponse<List<SonarrShow>>("/api/v3/series", It.IsAny<IntegrationSettingKeys>()))
            .ReturnsAsync(new List<SonarrShow> { new() { Id = 42, Title = "Show" } });
        integration
            .Setup(i => i.GetApiResponse<List<SonarrEpisode>>("/api/v3/episode?seriesId=42", It.IsAny<IntegrationSettingKeys>()))
            .ReturnsAsync(new List<SonarrEpisode> { Episode(1, 2), Episode(2, 0), Episode(3, 2) });

        var shows = await new SonarrService(integration.Object).GetShows();

        Assert.Equal(new[] { 0, 2 }, shows!.Single().Seasons.Select(s => s.SeasonNumber));
    }

    [Fact]
    public async Task GetShows_KeepsSeasons_WhenPresent()
    {
        var integration = new Mock<IIntegrationService>();
        integration
            .Setup(i => i.GetApiResponse<List<SonarrShow>>("/api/v3/series", It.IsAny<IntegrationSettingKeys>()))
            .ReturnsAsync(new List<SonarrShow>
            {
                new() { Id = 1, Title = "Show", Seasons = new List<SonarrSeason> { new() { SeasonNumber = 1 } } }
            });

        await new SonarrService(integration.Object).GetShows();

        integration.Verify(
            i => i.GetApiResponse<List<SonarrEpisode>>(It.IsAny<string>(), It.IsAny<IntegrationSettingKeys>()),
            Times.Never);
    }

    private static SonarrEpisode Episode(long id, int season) => new()
    {
        Id = id,
        EpisodeNumber = (int)id,
        Title = $"Episode {id}",
        SeasonNumber = season,
        HasFile = true
    };
}
