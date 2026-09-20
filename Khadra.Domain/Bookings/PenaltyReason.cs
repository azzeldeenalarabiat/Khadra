using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings;

/// <summary>
/// Why the platform assessed a penalty — or assessed that nothing is owed — as a stable code.
/// </summary>
/// <remarks>
/// <para>
/// Every <see cref="PenaltyAssessment"/> is written by this domain, never typed by a person, so its
/// reason comes from a closed set. It used to travel as an English sentence and nothing else, which
/// left the consoles printing English on an Arabic screen with no way to translate it: a sentence
/// cannot be looked up, and parsing one in the browser would be guessing.
/// </para>
/// <para>
/// Each member carries the sentence it replaces, byte for byte, and that sentence is STILL stored on
/// every new assessment. Older bookings hold the sentence alone and read exactly as they always did;
/// a client words the code when it has one and falls back to the frozen sentence when it does not.
/// Nothing is migrated, and no historical record is rewritten for a translation.
/// </para>
/// <para>
/// <b>The names are persisted contract.</b> The code is saved inside the <c>bookings.penalty</c> JSON
/// document by <see cref="Enumeration.Name"/>, and <c>FromName</c> throws on a name it does not know —
/// which would make a booking unloadable. A member is therefore never renamed and never removed, and
/// there is deliberately no catch-all member: a new reason is a new member, with its own sentence.
/// </para>
/// </remarks>
public sealed class PenaltyReason : Enumeration
{
    public static readonly PenaltyReason PaymentWindowLapsed =
        new(1, nameof(PaymentWindowLapsed), "The deposit was not paid within the payment window.");

    public static readonly PenaltyReason DealerAnswerWindowLapsed =
        new(2, nameof(DealerAnswerWindowLapsed), "The dealer did not answer within the agreed window.");

    public static readonly PenaltyReason DealerRejected =
        new(3, nameof(DealerRejected), "The dealer rejected the request.");

    public static readonly PenaltyReason DealerDidNotHandOver =
        new(4, nameof(DealerDidNotHandOver), "The dealer did not hand over the vehicle after approving the booking.");

    public static readonly PenaltyReason CustomerNoShow =
        new(5, nameof(CustomerNoShow), "The customer did not collect the vehicle within the no-show window.");

    public static readonly PenaltyReason DeliveryNoShowUndetermined =
        new(6, nameof(DeliveryNoShowUndetermined), "The vehicle was never handed over on a delivery booking; responsibility is undetermined.");

    public static readonly PenaltyReason CancelledBeforeDeposit =
        new(7, nameof(CancelledBeforeDeposit), "Cancelled before the deposit was paid.");

    public static readonly PenaltyReason CancelledInFreeWindow =
        new(8, nameof(CancelledInFreeWindow), "Cancelled inside the free cancellation window.");

    public static readonly PenaltyReason CustomerCancelledAfterFreeWindow =
        new(9, nameof(CustomerCancelledAfterFreeWindow), "The customer cancelled after the free cancellation window.");

    public static readonly PenaltyReason DealerCancelledAfterFreeWindow =
        new(10, nameof(DealerCancelledAfterFreeWindow), "The dealer cancelled after the free cancellation window.");

    public static readonly PenaltyReason CancelledByPlatform =
        new(11, nameof(CancelledByPlatform), "Cancelled by the platform.");

    /// <summary>
    /// Why a booking cannot be cancelled at all — a PREVIEW answer, never a recorded assessment.
    /// </summary>
    /// <remarks>
    /// <see cref="CancellationPreview"/> answers "what would cancelling cost" with an assessment
    /// object, and for a booking past cancelling there is nothing to cost. It is in this enum because
    /// it reaches a screen through the same field; it is never persisted, because nothing assesses it.
    /// </remarks>
    public static readonly PenaltyReason NotCancellable =
        new(12, nameof(NotCancellable), "This booking can no longer be cancelled.");

    private PenaltyReason(int id, string name, string sentence) : base(id, name)
    {
        Sentence = sentence;
    }

    /// <summary>The reason with this name, or null when this build has never heard of it.</summary>
    /// <remarks>
    /// Deliberately NOT <see cref="Enumeration.FromName{T}"/>, which throws. This is read back out of
    /// a stored booking, and a throw there would make the BOOKING unloadable — its screen, its
    /// cancellation preview, its dispute workspace, any sweep that loads it — because a build that
    /// had not heard of a newer reason could not materialise the aggregate at all. Nothing in the
    /// domain branches on this code: it is a label for a client, and "no code" already means "show the
    /// frozen sentence", which is the honest answer for a name this build cannot word.
    /// </remarks>
    public static PenaltyReason? Find(string? name) =>
        name is null ? null : GetAll<PenaltyReason>().SingleOrDefault(reason => reason.Name == name);

    /// <summary>
    /// The English sentence stored beside the code, unchanged from before codes existed.
    /// </summary>
    /// <remarks>
    /// Kept in the domain rather than the console because it is what gets WRITTEN onto a booking: the
    /// record has to read the same for a booking assessed today as for one assessed last year, and a
    /// client that has no words for a code falls back to exactly this text.
    /// </remarks>
    public string Sentence { get; }
}
