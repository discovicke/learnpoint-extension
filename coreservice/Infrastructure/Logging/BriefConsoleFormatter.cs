using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace coreservice.Infrastructure.Logging;

// Kompakt enradsformat: "HH:mm:ss meddelande".
// Våra egna meddelanden bär redan [Tagg]-prefix ([Scraper], [Trigger],
// [EventBus] ...), så kategorinamnet skrivs bara ut för Warning+,
// där sammanhanget behövs för att förstå var felet kommer ifrån.
public sealed class BriefConsoleFormatter : ConsoleFormatter
{
    public BriefConsoleFormatter() : base("brief") { }

    public override void Write<TState>(
        in LogEntry<TState> entry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var message = entry.Formatter(entry.State, entry.Exception);

        if (entry.LogLevel >= LogLevel.Warning)
        {
            var shortCategory = entry.Category?.Split('.').LastOrDefault() ?? "?";
            var level = entry.LogLevel switch
            {
                LogLevel.Warning => "WARN",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "CRITICAL",
                _ => entry.LogLevel.ToString().ToUpperInvariant(),
            };
            textWriter.WriteLine($"{timestamp} {level} [{shortCategory}] {message}");
        }
        else
        {
            textWriter.WriteLine($"{timestamp} {message}");
        }

        if (entry.Exception is not null)
            textWriter.WriteLine(entry.Exception);
    }
}
