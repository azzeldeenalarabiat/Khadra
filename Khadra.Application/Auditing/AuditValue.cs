using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Khadra.Application.Auditing;

/// <summary>
/// A value for the audit trail's Change column made of PARTS rather than words (pre-launch items 50 and 174).
/// </summary>
/// <remarks>
/// <para>
/// <c>previous_value</c> and <c>new_value</c> are kept for ever in a table that refuses UPDATE, and every administrator
/// reads them in their own language. A machine name — a status, a role, a reason code — the console words like every
/// other status. A value made of several facts (a dispute decision's figures, a handover's outcome, a city's two names)
/// used to be composed here as an English sentence, which no Arabic screen could word and no later build could
/// rephrase. It is stored instead as a compact JSON object of those facts, and the console composes the line for the
/// action it belongs to. Rows written before 2026-10-08 keep their sentences and are shown exactly as stored.
/// </para>
/// <para>
/// Figures travel as strings at the currency's full scale, so "18.000" reads like every other amount on the platform
/// and does not depend on how a JSON number would be printed.
/// </para>
/// </remarks>
public static class AuditValue
{
    // Arabic as itself rather than \u escapes: the column is capped, and somebody reading the table should be able to.
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    public static string Of<T>(T parts) => JsonSerializer.Serialize(parts, Options);
}
