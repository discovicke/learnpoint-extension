namespace coreservice.Interfaces;

public sealed record BuggernautRunResult(bool Success, string? Title, string? ClassName, string Output);

public interface IBuggernautService
{
    Task<BuggernautRunResult> GenerateAsync(string topic, string category, string difficulty, bool dryRun = false);
}
