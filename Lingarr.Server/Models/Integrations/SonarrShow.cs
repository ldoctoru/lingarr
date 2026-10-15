using System.Text.Json.Serialization;

namespace Lingarr.Server.Models.Integrations;

public class SonarrShow
{
    [JsonPropertyName("id")]
    public required int Id { get; set; }
    [JsonPropertyName("title")]
    public required string Title { get; set; }
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
    [JsonPropertyName("added")]
    public string? Added { get; set; }
    [JsonPropertyName("seasonFolder")]
    public bool SeasonFolder { get; set; } = true;
    [JsonPropertyName("rootFolderPath")]
    public string? RootFolderPath { get; set; } 
    [JsonPropertyName("seasons")]
    public List<SonarrSeason> Seasons { get; set; } = new();
    [JsonPropertyName("images")]
    public List<IntegrationImage>? Images { get; set; } = new();
}