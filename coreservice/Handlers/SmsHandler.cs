using System.Net.Http.Headers;
using System.Text;
using coreservice.Events;
using coreservice.Infrastructure.Data;
using coreservice.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace coreservice.Handlers;

public class SmsHandler(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<SmsHandler> logger)
    : IEventHandler<WeekSummarizedEvent>
{
    public async Task Handle(WeekSummarizedEvent @event)
    {
        try
        {
            var username = config["Elks:Username"];
            var password = config["Elks:Password"];
            var from = config["Elks:From"];

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(from))
            {
                logger.LogWarning("[Sms] ⚠ 46elks inte konfigurerat (Elks:Username/Password/From saknas) - hoppar över SMS för '{Title}'",
                    @event.Title);
                return;
            }

            if (!IsValidFrom(from, out var fromError))
            {
                logger.LogError("[Sms] ✗ Ogiltig Elks:From ('{From}'): {Reason} - uppdatera secret: dotnet user-secrets set \"Elks:From\" \"...\" --project coreservice",
                    from, fromError);
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recipients = await db.Subscribers
                .OrderBy(s => s.Id)
                .Select(s => s.PhoneNumber)
                .ToListAsync();

            if (recipients.Count == 0)
            {
                logger.LogInformation("[Sms]   Inga prenumeranter registrerade - inget SMS för '{Title}'", @event.Title);
                return;
            }

            var message = BuildMessage(@event.Title, @event.AiSummary);
            var sent = 0;
            var failed = 0;

            foreach (var to in recipients)
            {
                try
                {
                    await SendSmsAsync(username, password, from, to, message);
                    sent++;
                }
                catch (Exception ex)
                {
                    failed++;
                    logger.LogError(ex, "[Sms]   ✗ Misslyckades med SMS till {To}", to);
                }
            }

            logger.LogInformation("[Sms] ✓ '{Title}' - {Sent} skickade, {Failed} misslyckade ({Total} prenumeranter)",
                @event.Title, sent, failed, recipients.Count);
        }
        catch (Exception ex)
        {
            // Får aldrig kasta - BuggernautHandler ska köras oavsett.
            logger.LogError(ex, "[Sms] ✗ Oväntat fel vid SMS-utskick för '{Title}'", @event.Title);
        }
    }

    // Max 160 tecken, GSM-7-säkert (utan unicode-tecken och radbrytningar som
    // tvingar UCS-2 och kapar gränsen till 70 tecken).
    private static string BuildMessage(string title, string summary)
    {
        const int maxLength = 160;

        var cleanTitle = GsmSafe(CollapseWhitespace(title));
        var teaser = GsmSafe(CollapseWhitespace(summary));

        var head = $"Nytt tema: {cleanTitle}";
        if (head.Length > 100)
            head = $"Nytt tema: {TruncateWord(head["Nytt tema: ".Length..], 100 - "Nytt tema: ".Length)}";

        var rest = maxLength - head.Length - 3; // " - "
        if (rest < 10)
            return TruncateWord(head, maxLength);

        return $"{head} - {TruncateWord(teaser, rest)}";
    }

    private static string CollapseWhitespace(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    private static string GsmSafe(string text) =>
        text.Replace('–', '-').Replace('—', '-').Replace("…", "...").Replace('´', '\'');

    private static string TruncateWord(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        var cut = text[..(maxLength - 3)].TrimEnd();
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
            cut = cut[..lastSpace];

        return cut + "...";
    }

    private static bool IsValidFrom(string from, out string reason)
    {
        // 46elks-regler: alfanumerisk avsändare max 11 tecken, numerisk max 15 siffror.
        if (from.StartsWith('+'))
        {
            var digits = from[1..];
            if (digits.Length is >= 7 and <= 15 && digits.All(char.IsDigit))
            {
                reason = "";
                return true;
            }
            reason = "numerisk avsändare måste vara + följt av 7-15 siffror";
            return false;
        }

        if (from.All(char.IsDigit))
        {
            if (from.Length is >= 7 and <= 15)
            {
                reason = "";
                return true;
            }
            reason = "numerisk avsändare måste vara 7-15 siffror";
            return false;
        }

        if (from.Length <= 11)
        {
            reason = "";
            return true;
        }

        reason = $"alfanumerisk avsändare får vara max 11 tecken (nu {from.Length})";
        return false;
    }

    private async Task SendSmsAsync(string username, string password, string from, string to, string message)
    {
        var client = httpFactory.CreateClient();
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["from"] = from,
            ["to"] = to,
            ["message"] = message,
        });

        using var response = await client.PostAsync("https://api.46elks.com/a1/sms", content);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"46elks svarade {(int)response.StatusCode}: {body}");
        }

        logger.LogDebug("[Sms]   SMS skickat till {To}", to);
    }
}
