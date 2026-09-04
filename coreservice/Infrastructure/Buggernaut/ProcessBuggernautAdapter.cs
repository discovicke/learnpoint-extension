using System.Diagnostics;
using System.Text.RegularExpressions;
using coreservice.Interfaces;

namespace coreservice.Infrastructure.Buggernaut;

public partial class ProcessBuggernautAdapter : IBuggernautService
{
    private readonly string _workingDir;
    private readonly string _command;
    private readonly string _outputDir;
    private readonly string? _llmProvider;
    private readonly string? _llmApiKey;
    private readonly int _timeoutSeconds;
    private readonly ILogger<ProcessBuggernautAdapter> _logger;

    public ProcessBuggernautAdapter(IConfiguration config, IWebHostEnvironment env,
        ILogger<ProcessBuggernautAdapter> logger)
    {
        _logger = logger;
        _command = config.GetValue<string>("Buggernaut:Command") ?? "dotnet";

        _workingDir = config.GetValue<string>("Buggernaut:WorkingDirectory")
                      ?? Directory.GetParent(env.ContentRootPath)?.FullName
                      ?? env.ContentRootPath;

        _outputDir = config.GetValue<string>("Buggernaut:OutputDirectory")
                     ?? Path.Combine(_workingDir, "exercises");

        _llmProvider = config.GetValue<string>("Buggernaut:LlmProvider");
        _llmApiKey = config.GetValue<string>("Buggernaut:LlmApiKey");
        _timeoutSeconds = config.GetValue("Buggernaut:TimeoutSeconds", 600);
    }

    public async Task<BuggernautRunResult> GenerateAsync(string topic, string category, string difficulty,
        bool dryRun = false)
    {
        _logger.LogInformation(
            "[Buggernaut] Kör generate (kategori={Category}, svårighetsgrad={Difficulty}, dryRun={DryRun})",
            category, difficulty, dryRun);

        var psi = new ProcessStartInfo
        {
            FileName = _command,
            WorkingDirectory = _workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("buggernaut");
        psi.ArgumentList.Add("generate");
        psi.ArgumentList.Add("-t");
        psi.ArgumentList.Add(topic);
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(_outputDir);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(category);
        psi.ArgumentList.Add("-d");
        psi.ArgumentList.Add(difficulty);
        if (dryRun)
            psi.ArgumentList.Add("--dry-run");


        if (!string.IsNullOrWhiteSpace(_llmProvider))
            psi.Environment["LLM__Provider"] = _llmProvider;
        if (!string.IsNullOrWhiteSpace(_llmProvider) && !string.IsNullOrWhiteSpace(_llmApiKey))
            psi.Environment[$"LLM__{_llmProvider}__ApiKey"] = _llmApiKey;

        var stopwatch = Stopwatch.StartNew();

        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException("Kunde inte starta Buggernaut-processen");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignored
            }

            throw new TimeoutException($"Buggernaut-anropet tog längre än {_timeoutSeconds}s och avbröts.");
        }

        stopwatch.Stop();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (FailureMarker().IsMatch(stdout))
        {
            _logger.LogWarning("[Buggernaut] ✗ Genereringen misslyckades ({Elapsed}ms)", stopwatch.ElapsedMilliseconds);
            return new BuggernautRunResult(false, null, null, stdout);
        }

        var title = SuccessTitle().Match(stdout) is { Success: true } titleMatch
            ? titleMatch.Groups[1].Value
            : null;
        var className = ExerciseFile().Match(stdout) is { Success: true } fileMatch
            ? fileMatch.Groups[1].Value
            : null;

        if (title is null)
        {
            _logger.LogWarning("[Buggernaut] ✗ Okänd utdata, stderr:\n{Stderr}", stderr);
            return new BuggernautRunResult(false, null, null, stdout);
        }

        _logger.LogInformation("[Buggernaut] ✓ Övning genererad: \"{Title}\" ({ClassName}, {Elapsed}ms)",
            title, className ?? "?", stopwatch.ElapsedMilliseconds);
        return new BuggernautRunResult(true, title, className, stdout);
    }
    
    // Här får jag tacka AI för hur jag hämtar ut Buggernaut-generering så loggningen fungerade, det var ett helvete att försöka lösa själv
    // Om det funkar så funkar det, orkar inte bråka mer med det.
    // Har du en smidigare lösning så tackar jag gärna ja till hjälp!

    [GeneratedRegex("Kunde inte generera en giltig övning")]
    private static partial Regex FailureMarker();

    [GeneratedRegex("Övning genererad:\\s*\"([^\"]+)\"")]
    private static partial Regex SuccessTitle();

    [GeneratedRegex(@"Buggernaut\.Exercises[\\/](\w+)\.cs")]
    private static partial Regex ExerciseFile();
}