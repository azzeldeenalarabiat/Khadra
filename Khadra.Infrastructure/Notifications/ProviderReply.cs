using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Khadra.Infrastructure.Notifications;

/// <summary>
/// Turns what a mail provider said into something a log line can carry.
/// </summary>
/// <remarks>
/// Two very different moments pass through here. After an ACCEPTANCE nothing may throw: a reply in a
/// shape nobody expected costs the log an id, and must never turn a message that went into one the
/// caller is told did not — that caller is a registration screen, and it would tell somebody an email
/// failed that is already in their inbox. After a REFUSAL the text goes into an exception that is
/// logged at Error, and a refusal is exactly where providers name mailboxes.
/// </remarks>
internal static partial class ProviderReply
{
    /// <summary>The most of an accepting provider's reply that a log line carries.</summary>
    public const int MaxLength = 200;

    /// <summary>
    /// The most of a refusal that an Error line carries. Longer than an acceptance, because the reason
    /// is the part an operator needs and providers put it after a code.
    /// </summary>
    public const int MaxRefusalLength = 500;

    /// <summary>The string property <paramref name="name"/> of a JSON object body, or null.</summary>
    public static string? JsonString(string? body, string name)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(name, out var value)
                   && value.ValueKind == JsonValueKind.String
                ? Clean(value.GetString(), MaxLength)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A relay's reply to a message it ACCEPTED, fit for a log: the recipient's address taken out,
    /// control characters flattened, and cut to <see cref="MaxLength"/>.
    /// </summary>
    /// <remarks>
    /// Only the recipient's address, because the rest of an acceptance can be the id the message is
    /// traced by, and a relay writes that id in the shape of an address:
    /// <c>queued as &lt;202609170930.4242@smtp-relay.mailin.fr&gt;</c>. It comes out BEFORE the cut,
    /// so an address the cut would have split cannot leave half of itself behind.
    /// </remarks>
    public static string? Text(string? text, string recipientAddress)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return Clean(
            string.IsNullOrWhiteSpace(recipientAddress)
                ? text
                : text.Replace(recipientAddress, "[recipient]", StringComparison.OrdinalIgnoreCase),
            MaxLength);
    }

    /// <summary>
    /// A provider's REFUSAL, fit for an Error log: every address taken out, control characters
    /// flattened, and cut to <see cref="MaxRefusalLength"/>.
    /// </summary>
    /// <remarks>
    /// Every address, not only the recipient's. A refusal names mailboxes — the recipient a relay
    /// rejected (<c>5.1.1 &lt;ali@example.com&gt;: Recipient address rejected</c>), the sender a
    /// provider has not verified, the account owner a free tier will only write to — and none of them
    /// is what an operator needs from the line. The reason is, and it survives.
    /// </remarks>
    public static string? Refusal(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : Clean(Address().Replace(text, "[address]"), MaxRefusalLength);

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        var cleaned = new StringBuilder(Math.Min(trimmed.Length, maxLength));
        foreach (var character in trimmed)
        {
            if (cleaned.Length == maxLength)
                break;

            // A line break inside a logged value is how one log line becomes two, with the second
            // saying whatever the provider's text said.
            cleaned.Append(char.IsControl(character) ? ' ' : character);
        }

        return cleaned.ToString();
    }

    /// <summary>Anything shaped like an address: no spaces, brackets, quotes or separators around an @.</summary>
    [GeneratedRegex("""[^\s<>()\[\]{},;:"']+@[^\s<>()\[\]{},;:"']+""")]
    private static partial Regex Address();
}
