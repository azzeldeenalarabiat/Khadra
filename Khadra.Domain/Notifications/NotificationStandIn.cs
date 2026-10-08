using Khadra.Domain.Common;

namespace Khadra.Domain.Notifications;

/// <summary>
/// Who a notification's actor is when it is NOT named (pre-launch item 103): a customer, who is never named to an office;
/// a colleague whose account could not be read; a rental office that has left the platform.
/// </summary>
/// <remarks>
/// <para>
/// Those rows used to carry only an English phrase in <c>actor_name</c> ("A customer", "A colleague", "The rental
/// office"), written at the moment of the action into a table that is never rewritten, so an Arabic screen could word
/// them only by recognising the English. The code is stored BESIDE the phrase and the clients word the code.
/// </para>
/// <para>
/// The phrase is still written, unchanged, as <see cref="LegacyName"/>: <c>actorName</c> is a field every installed
/// customer app already reads and prints, and replacing it would be a breaking change to that contract. Rows written
/// before 2026-10-08 have the phrase and no code.
/// </para>
/// <para>
/// Stored by name, ADD-ONLY: a member is never renamed or removed once a row may hold it (pre-launch item 19).
/// </para>
/// </remarks>
public sealed class NotificationStandIn : Enumeration
{
    public static readonly NotificationStandIn Customer = new(1, "Customer", "A customer");
    public static readonly NotificationStandIn Colleague = new(2, "Colleague", "A colleague");
    public static readonly NotificationStandIn RentalOffice = new(3, "RentalOffice", "The rental office");

    private NotificationStandIn(int id, string name, string legacyName) : base(id, name) => LegacyName = legacyName;

    /// <summary>The English phrase written in <c>actor_name</c> beside the code, exactly as before it existed.</summary>
    public string LegacyName { get; }
}
