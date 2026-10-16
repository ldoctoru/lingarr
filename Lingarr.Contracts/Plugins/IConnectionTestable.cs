namespace Lingarr.Contracts.Plugins;

/// <summary>
/// Optional interface for translation providers that can verify their configuration
/// (credentials, endpoint, model) without running a translation job.
/// The settings UI shows a "Test connection" action for providers that implement it.
/// </summary>
public interface IConnectionTestable
{
    Task<PluginConnectionResult> TestConnectionAsync(CancellationToken cancellationToken);
}

public sealed class PluginConnectionResult
{
    public required bool Success { get; init; }
    public string? Message { get; init; }

    public static PluginConnectionResult Ok(string? message = null) =>
        new() { Success = true, Message = message };

    public static PluginConnectionResult Fail(string message) =>
        new() { Success = false, Message = message };
}
