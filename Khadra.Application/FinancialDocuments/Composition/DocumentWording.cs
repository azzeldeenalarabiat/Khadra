using System.Globalization;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;

namespace Khadra.Application.FinancialDocuments.Composition;

/// <summary>
/// Every word an issued financial document shows, in English and Arabic (payments Phase 5). The ONE
/// place a document is worded: a document is a record, so its words are frozen with its figures when it
/// is issued, and web, app, console and the Phase 6 PDF render what is stored.
/// </summary>
/// <remarks>
/// <para>
/// Changing a sentence here changes documents issued AFTER the change and never one already issued. A
/// wording mistake in an issued document is corrected the way any mistake is: a void and a correction.
/// </para>
/// <para>
/// The language follows the customer app's rules: Latin digits, absolute times on the Amman clock, and its
/// Arabic vocabulary — مكتب التأجير for the rental office, سيارة for the car, خضرا for Khadra, عربون for the
/// deposit. Words the customer already reads on the booking page (refund reasons, deposit sentences, the
/// penalty's standing from pre-launch item 173) are the same words here. Nothing is ever called a tax
/// invoice (owner, 2026-09-27).
/// </para>
/// <para>
/// A run that reads left to right inside an Arabic sentence — an amount, a date, a reference, an office's
/// registered name — is wrapped in Unicode isolates (<see cref="Isolate"/>), so it can never reorder the
/// sentence around it.
/// </para>
/// </remarks>
internal static class DocumentWording
{
    // ── Document ───────────────────────────────────────────────────────────────────────────────────

    public static readonly BilingualText NotATaxInvoice =
        BilingualText.Of("This document is not a tax invoice.", "هذا المستند ليس فاتورة ضريبية.");

    public static readonly BilingualText TimeNote =
        BilingualText.Of("All times are Amman time.", "جميع الأوقات بتوقيت عمّان.");

    public static BilingualText Title(FinancialDocumentType type) =>
        type == FinancialDocumentType.PaymentReceipt ? BilingualText.Of("Payment receipt", "إيصال دفع")
        : type == FinancialDocumentType.RefundReceipt ? BilingualText.Of("Refund receipt", "إيصال استرداد")
        : type == FinancialDocumentType.BookingStatement ? BilingualText.Of("Booking statement", "كشف حساب الحجز")
        : throw new ArgumentOutOfRangeException(nameof(type), type.Name, "Not a financial document type.");

    public static BilingualText Cause(FinancialDocumentCause cause) =>
        cause == FinancialDocumentCause.PaymentCaptured ? BilingualText.Of("Payment received", "استلام دفعة")
        : cause == FinancialDocumentCause.RefundSettled ? BilingualText.Of("Refund completed", "إتمام استرداد")
        : cause == FinancialDocumentCause.DisputeResolved ? BilingualText.Of("Dispute decided", "حسم نزاع")
        : cause == FinancialDocumentCause.BookingEnded ? BilingualText.Of("Booking ended", "انتهاء الحجز")
        : cause == FinancialDocumentCause.CashRecorded
            ? BilingualText.Of("Cash recorded by the rental office", "تسجيل مبلغ نقدي من مكتب التأجير")
        : cause == FinancialDocumentCause.Correction
            ? BilingualText.Of("Correction of a voided document", "تصحيح لمستند أُبطل")
        : cause == FinancialDocumentCause.ReceiptCorrected ? BilingualText.Of("Receipt corrected", "تصحيح إيصال")
        // Pre-launch item 212 (owner, 2026-09-30), in the words of the owner's approved sentence below: «العربون» is the
        // booking deposit, «التأمين» only ever the security deposit the office holds.
        : cause == FinancialDocumentCause.PenaltyKept ? BilingualText.Of("Deposit penalty finalized", "تثبيت حسم العربون")
        : throw new ArgumentOutOfRangeException(nameof(cause), cause.Name, "Not a document cause.");

    /// <summary>
    /// What a customer is told once the ledger has kept their penalty from the deposit: the owner's own words
    /// (2026-09-30), said ONCE — in the Penalty section, as in the booking page's penalty notice. The Deposit section
    /// states only the amount and where it went (<see cref="Deposit"/>).
    /// </summary>
    public static readonly BilingualText PenaltyKeptFromDeposit = BilingualText.Of(
        "The dispute window ended without a dispute. The assessed deposit penalty has now been finalized and applied according to the booking’s cancellation terms.",
        "انتهت مهلة النزاع دون فتح نزاع. تم تثبيت حسم العربون وتطبيقه وفق شروط إلغاء الحجز.");

    public static class Headings
    {
        public static readonly BilingualText Document = BilingualText.Of("Document", "المستند");
        public static readonly BilingualText Parties = BilingualText.Of("Issued by and to", "الجهة المُصدِرة والعميل");
        public static readonly BilingualText Booking = BilingualText.Of("Booking", "الحجز");
        public static readonly BilingualText Payment = BilingualText.Of("Payment", "الدفعة");
        public static readonly BilingualText YourBooking = BilingualText.Of("Your booking after this payment", "حجزك بعد هذه الدفعة");
        public static readonly BilingualText Refund = BilingualText.Of("Refund", "الاسترداد");
        public static readonly BilingualText OriginalPayment = BilingualText.Of("Original payment", "الدفعة الأصلية");
        public static readonly BilingualText Charges = BilingualText.Of("Booking charges", "رسوم الحجز");
        public static readonly BilingualText PaidOnline = BilingualText.Of("Paid online", "المدفوع عبر الإنترنت");
        public static readonly BilingualText Refunds = BilingualText.Of("Refunds", "المبالغ المستردة");
        public static readonly BilingualText Deposit = BilingualText.Of("Deposit", "العربون");
        public static readonly BilingualText Penalty = BilingualText.Of("Penalty assessed", "الجزاء المقدَّر");
        public static readonly BilingualText Balance = BilingualText.Of("Balance", "المتبقي");
        public static readonly BilingualText Totals = BilingualText.Of("Totals", "الإجماليات");
        public static readonly BilingualText Documents = BilingualText.Of("Documents", "المستندات");
    }

    public static class Labels
    {
        public static readonly BilingualText Number = BilingualText.Of("Document number", "رقم المستند");
        public static readonly BilingualText Version = BilingualText.Of("Version", "الإصدار");
        public static readonly BilingualText IssuedAt = BilingualText.Of("Issued", "تاريخ الإصدار");
        public static readonly BilingualText IssuedBecause = BilingualText.Of("Issued because of", "سبب الإصدار");
        public static readonly BilingualText PreviousVersion = BilingualText.Of("Replaces version", "يحلّ محلّ الإصدار");
        public static readonly BilingualText Corrects = BilingualText.Of("Corrects voided document", "يصحّح المستند المُبطل");
        public static readonly BilingualText CoversUntil = BilingualText.Of("Covers money movements up to", "يشمل الحركات المالية حتى");

        public static readonly BilingualText IssuedBy = BilingualText.Of("Issued by", "صادر عن");
        public static readonly BilingualText CommercialRegistration = BilingualText.Of("Commercial registration", "السجل التجاري");
        public static readonly BilingualText Address = BilingualText.Of("Address", "العنوان");
        public static readonly BilingualText SupportEmail = BilingualText.Of("Support email", "بريد الدعم");
        public static readonly BilingualText SupportPhone = BilingualText.Of("Support phone", "هاتف الدعم");
        public static readonly BilingualText Customer = BilingualText.Of("Customer", "العميل");

        public static readonly BilingualText Reference = BilingualText.Of("Booking reference", "الرقم المرجعي");
        public static readonly BilingualText BookingStatus = BilingualText.Of("Booking status", "حالة الحجز");
        public static readonly BilingualText Office = BilingualText.Of("Rental office", "مكتب التأجير");
        public static readonly BilingualText OfficeRegistration = BilingualText.Of("Rental office's commercial registration", "السجل التجاري لمكتب التأجير");
        public static readonly BilingualText OfficeLocation = BilingualText.Of("Rental office's location", "موقع مكتب التأجير");
        public static readonly BilingualText Car = BilingualText.Of("Car", "السيارة");
        public static readonly BilingualText CarType = BilingualText.Of("Car type", "فئة السيارة");
        public static readonly BilingualText Plate = BilingualText.Of("Plate number", "رقم اللوحة");
        public static readonly BilingualText RentalStart = BilingualText.Of("Rental starts", "بداية الإيجار");
        public static readonly BilingualText RentalEnd = BilingualText.Of("Rental ends", "نهاية الإيجار");
        public static readonly BilingualText RentalDays = BilingualText.Of("Rental days", "أيام الإيجار");
        public static readonly BilingualText PickupMethod = BilingualText.Of("Pickup method", "طريقة الاستلام");

        public static readonly BilingualText PaymentFor = BilingualText.Of("Payment for", "نوع الدفعة");
        public static readonly BilingualText PaidOn = BilingualText.Of("Paid on", "تاريخ الدفع");
        public static readonly BilingualText AmountPaid = BilingualText.Of("Amount paid", "المبلغ المدفوع");
        public static readonly BilingualText ProcessingFee = BilingualText.Of("Card processing fee, included", "رسوم معالجة البطاقة، مشمولة");
        public static readonly BilingualText FeeRefundability = BilingualText.Of("Processing fee", "رسوم المعالجة");
        public static readonly BilingualText AppliedToBooking = BilingualText.Of("Applied to the booking", "المحتسب على الحجز");
        public static readonly BilingualText ToBeRefunded = BilingualText.Of("To be refunded to you", "المبلغ الذي يُسترد لك");
        public static readonly BilingualText BookingTotal = BilingualText.Of("Booking total", "إجمالي الحجز");
        public static readonly BilingualText RequiredDeposit = BilingualText.Of("Required deposit", "العربون المطلوب");
        public static readonly BilingualText PaidOnlineToDate = BilingualText.Of("Paid online towards the booking to date", "المدفوع عبر الإنترنت للحجز حتى تاريخه");
        public static readonly BilingualText BalanceDueAtPickup = BilingualText.Of("Balance due to the rental office at pickup", "المتبقي المستحق لمكتب التأجير عند الاستلام");
        public static readonly BilingualText RemainingBalance = BilingualText.Of("Remaining balance", "المبلغ المتبقي");

        public static readonly BilingualText Reason = BilingualText.Of("Reason", "السبب");
        public static readonly BilingualText AmountRefunded = BilingualText.Of("Amount refunded", "المبلغ المسترد");
        public static readonly BilingualText BookingMoneyInRefund = BilingualText.Of("Booking money in it", "منه من مبلغ الحجز");
        public static readonly BilingualText FeeInRefund = BilingualText.Of("Card processing fee in it", "منه رسوم معالجة البطاقة");
        public static readonly BilingualText RefundRecordedAt = BilingualText.Of("Refund recorded", "تاريخ تسجيل الاسترداد");
        public static readonly BilingualText RefundedAt = BilingualText.Of("Refunded", "تاريخ الاسترداد");
        public static readonly BilingualText DisputeDecidedAt = BilingualText.Of("Dispute decided", "تاريخ حسم النزاع");
        public static readonly BilingualText PaymentReceipt = BilingualText.Of("Payment receipt", "إيصال الدفع");
        public static readonly BilingualText RefundedFromPaymentToDate = BilingualText.Of("Refunded from this payment to date", "المسترد من هذه الدفعة حتى تاريخه");
        public static readonly BilingualText RefundStatus = BilingualText.Of("Where it stands", "حالته");

        public static readonly BilingualText DeliveryFee = BilingualText.Of("Delivery fee", "رسوم التوصيل");
        public static readonly BilingualText SecurityDeposit =
            BilingualText.Of("Security deposit the rental office holds at pickup (not paid online)", "التأمين الذي يحتفظ به مكتب التأجير عند الاستلام (لا يُدفع عبر الإنترنت)");
        public static readonly BilingualText AmountAssessed = BilingualText.Of("Amount assessed", "المبلغ المقدَّر");
        public static readonly BilingualText CashAtPickup = BilingualText.Of("Cash the rental office recorded at pickup", "النقد الذي سجّله مكتب التأجير عند الاستلام");
        public static readonly BilingualText CashAtReturn = BilingualText.Of("Cash the rental office recorded at return", "النقد الذي سجّله مكتب التأجير عند الإرجاع");
        public static readonly BilingualText BalanceWasDueAtPickup = BilingualText.Of("Balance that was due to the rental office at pickup", "المبلغ الذي كان مستحقًا لمكتب التأجير عند الاستلام");

        public static readonly BilingualText ChargedOnline = BilingualText.Of("Charged online", "إجمالي ما خُصم عبر الإنترنت");
        public static readonly BilingualText RefundedToYou = BilingualText.Of("Refunded to you", "تم استرداده لك");
        public static readonly BilingualText RefundInProgress = BilingualText.Of("Refund in progress", "قيد الاسترداد");
        public static readonly BilingualText RefundDelayed = BilingualText.Of("Refund delayed — still owed", "استرداد متأخر ولا يزال مستحقًا");
        public static readonly BilingualText NetPaidOnline = BilingualText.Of("Net paid online", "صافي المدفوع عبر الإنترنت");
        public static readonly BilingualText PreviousStatement = BilingualText.Of("Previous version of this statement", "الإصدار السابق من هذا الكشف");
    }

    // ── Codes the customer reads ───────────────────────────────────────────────────────────────────

    public static BilingualText BookingStatus(BookingStatus status) =>
        status == Domain.Bookings.BookingStatus.Requested ? BilingualText.Of("Awaiting the rental office's answer", "بانتظار ردّ مكتب التأجير")
        : status == Domain.Bookings.BookingStatus.Approved ? BilingualText.Of("Approved — deposit due", "موافق عليه — العربون مستحق")
        : status == Domain.Bookings.BookingStatus.Confirmed ? BilingualText.Of("Confirmed", "مؤكد")
        : status == Domain.Bookings.BookingStatus.PickedUp ? BilingualText.Of("Car collected", "استُلمت السيارة")
        : status == Domain.Bookings.BookingStatus.Returned ? BilingualText.Of("Car returned", "أُعيدت السيارة")
        : status == Domain.Bookings.BookingStatus.Completed ? BilingualText.Of("Completed", "منتهٍ")
        : status == Domain.Bookings.BookingStatus.Cancelled ? BilingualText.Of("Cancelled", "ملغى")
        : status == Domain.Bookings.BookingStatus.Rejected ? BilingualText.Of("Rejected by the rental office", "رفضه مكتب التأجير")
        : status == Domain.Bookings.BookingStatus.NoShow ? BilingualText.Of("Car not collected", "لم تُستلم السيارة")
        : status == Domain.Bookings.BookingStatus.Expired ? BilingualText.Of("Expired", "منتهي الصلاحية")
        : throw new ArgumentOutOfRangeException(nameof(status), status.Name, "A booking status with no words.");

    public static BilingualText PickupMethod(PickupMethod method) =>
        method == Domain.Bookings.PickupMethod.SelfPickup ? BilingualText.Of("Collected from the rental office", "الاستلام من مكتب التأجير")
        : method == Domain.Bookings.PickupMethod.Delivery ? BilingualText.Of("Delivered by the rental office", "توصيل من مكتب التأجير")
        : throw new ArgumentOutOfRangeException(nameof(method), method.Name, "A pickup method with no words.");

    public static BilingualText Purpose(PaymentPurpose purpose) =>
        purpose == PaymentPurpose.Deposit ? BilingualText.Of("Deposit", "العربون")
        : purpose == PaymentPurpose.FullPayment ? BilingualText.Of("Full payment", "الدفع الكامل")
        : purpose == PaymentPurpose.RemainingBalance ? BilingualText.Of("Remaining balance", "المبلغ المتبقي")
        : throw new ArgumentOutOfRangeException(nameof(purpose), purpose.Name, "A payment purpose with no words.");

    public static readonly BilingualText NotApplied =
        BilingualText.Of("Payment not applied to the booking", "دفعة لم تُحتسب على الحجز");

    /// <summary>The website's own sentence for a capture that could not be applied (decision 7).</summary>
    public static readonly BilingualText OrphanNote = BilingualText.Of(
        "This payment could not be applied to your booking, so all of it is refunded to you.",
        "تعذّر احتساب هذه الدفعة على حجزك، لذلك يُسترد لك كامل مبلغها.");

    public static BilingualText FeeRefundable(bool refundable) =>
        refundable
            ? BilingualText.Of(
                "Refunded with the payment if the booking ends before the car is collected.",
                "تُسترد مع الدفعة إذا انتهى الحجز قبل استلام السيارة.")
            : BilingualText.Of("Not refundable.", "غير قابلة للاسترداد.");

    /// <summary>The booking page's own words for a refund's reason, so the two never differ.</summary>
    public static BilingualText RefundReason(RefundReason reason) =>
        reason == Domain.Payments.RefundReason.FreeCancellation ? BilingualText.Of("Free cancellation", "إلغاء مجاني")
        : reason == Domain.Payments.RefundReason.PlatformCancellation ? BilingualText.Of("Cancelled by Khadra", "ألغته خضرا")
        : reason == Domain.Payments.RefundReason.EndedBeforePickup ? BilingualText.Of("Paid above the deposit", "المدفوع فوق العربون")
        : reason == Domain.Payments.RefundReason.DisputeWindowClosed ? BilingualText.Of("Deposit returned", "إعادة العربون")
        : reason == Domain.Payments.RefundReason.DisputeResolution ? BilingualText.Of("Dispute decision", "قرار النزاع")
        : reason == Domain.Payments.RefundReason.OrphanedCapture ? BilingualText.Of("Payment that could not be used", "دفعة تعذّر استخدامها")
        : throw new ArgumentOutOfRangeException(nameof(reason), reason.Name, "A refund reason with no words.");

    public static BilingualText RefundStatus(RefundStatus status) =>
        status == Domain.Payments.RefundStatus.Settled ? BilingualText.Of("Refunded", "تم الاسترداد")
        : status == Domain.Payments.RefundStatus.Failed
            ? BilingualText.Of("Delayed — still owed, and being retried", "متأخر — لا يزال مستحقًا وتُعاد المحاولة")
        : status.IsOutstanding ? BilingualText.Of("On its way", "قيد الاسترداد")
        : throw new ArgumentOutOfRangeException(nameof(status), status.Name, "A refund status with no words.");

    public static readonly BilingualText BankMayTakeTime = BilingualText.Of(
        "Your bank may take additional time to show it.",
        "قد يحتاج البنك بعض الوقت لإظهار المبلغ في حسابك.");

    /// <summary>A penalty's reason, from its code; an assessment made before codes existed keeps its own sentence.</summary>
    public static BilingualText PenaltyReason(PenaltyReason? code, string sentence) =>
        code == Domain.Bookings.PenaltyReason.PaymentWindowLapsed
            ? BilingualText.Of("The deposit was not paid within the payment window.", "لم يُدفع العربون خلال مهلة الدفع.")
        : code == Domain.Bookings.PenaltyReason.DealerAnswerWindowLapsed
            ? BilingualText.Of("The rental office did not answer within the agreed window.", "لم يردّ مكتب التأجير خلال المهلة المتفق عليها.")
        : code == Domain.Bookings.PenaltyReason.DealerRejected
            ? BilingualText.Of("The rental office rejected the request.", "رفض مكتب التأجير الطلب.")
        : code == Domain.Bookings.PenaltyReason.DealerDidNotHandOver
            ? BilingualText.Of("The rental office did not hand over the car after approving the booking.", "لم يسلّم مكتب التأجير السيارة بعد موافقته على الحجز.")
        : code == Domain.Bookings.PenaltyReason.CustomerNoShow
            ? BilingualText.Of("The car was not collected within the no-show window.", "لم تُستلم السيارة خلال مهلة عدم الحضور.")
        : code == Domain.Bookings.PenaltyReason.DeliveryNoShowUndetermined
            ? BilingualText.Of("The car was never handed over on a delivery booking; responsibility is undetermined.", "لم تُسلَّم السيارة في حجز بالتوصيل، ولم تُحدَّد المسؤولية.")
        : code == Domain.Bookings.PenaltyReason.CancelledBeforeDeposit
            ? BilingualText.Of("Cancelled before the deposit was paid.", "أُلغي قبل دفع العربون.")
        : code == Domain.Bookings.PenaltyReason.CancelledInFreeWindow
            ? BilingualText.Of("Cancelled inside the free cancellation window.", "أُلغي خلال مهلة الإلغاء المجاني.")
        : code == Domain.Bookings.PenaltyReason.CustomerCancelledAfterFreeWindow
            ? BilingualText.Of("Cancelled by the customer after the free cancellation window.", "ألغاه العميل بعد انتهاء مهلة الإلغاء المجاني.")
        : code == Domain.Bookings.PenaltyReason.DealerCancelledAfterFreeWindow
            ? BilingualText.Of("Cancelled by the rental office after the free cancellation window.", "ألغاه مكتب التأجير بعد انتهاء مهلة الإلغاء المجاني.")
        : code == Domain.Bookings.PenaltyReason.CancelledByPlatform
            ? BilingualText.Of("Cancelled by Khadra.", "ألغته خضرا.")
        // No code (an assessment older than codes) or one this table does not know: the sentence the
        // platform froze on the booking, the same in both languages rather than a translation invented here.
        : BilingualText.Of(sentence, Isolate(sentence));

    /// <summary>
    /// Where the penalty stands — pre-launch item 173's sentences (owner, 2026-09-26). "See Payments", the
    /// booking page's pointer, becomes the section of this document that shows the decision.
    /// </summary>
    /// <param name="keptFromDeposit">
    /// The ledger kept the penalty from the deposit when the window closed with no dispute (payments Phase 8;
    /// owner, 2026-09-29; pre-launch item 164): the owner's approved sentence (2026-09-30).
    /// </param>
    public static BilingualText PenaltyStanding(bool resolvedByDispute, bool keptFromDeposit) =>
        resolvedByDispute
            ? BilingualText.Of(
                "This penalty was resolved through a dispute. The final amount is shown under Deposit.",
                "تم حسم هذا الجزاء من خلال نزاع. يظهر المبلغ النهائي في قسم العربون.")
            : keptFromDeposit
                ? PenaltyKeptFromDeposit
                : BilingualText.Of(
                    "A penalty has been assessed, but no amount has been charged yet.",
                    "تم تقدير جزاء، ولكن لم يتم خصم أي مبلغ بعد.");

    public static BilingualText Range(Money min, Money max) =>
        BilingualText.Of(
            $"Between {SnapshotJson.AmountText(min)} and {SnapshotJson.AmountText(max)}",
            $"بين {Isolate(SnapshotJson.AmountText(min))} و{Isolate(SnapshotJson.AmountText(max))}");

    // ── The deposit, in the booking page's sentences ───────────────────────────────────────────────

    /// <summary>
    /// The deposit's state as the booking page words it (<c>payments.deposit.*</c>), with its figures and
    /// dates written in. Pre-launch item 164's sentence is the owner's own.
    /// </summary>
    public static BilingualText Deposit(string state, Money amount, string? windowEnds, Money? share)
    {
        var en = SnapshotJson.AmountText(amount);
        var ar = Isolate(en);
        var dateEn = windowEnds ?? string.Empty;
        var dateAr = windowEnds is null ? string.Empty : Isolate(windowEnds);
        return state switch
        {
            DepositStates.Held => BilingualText.Of(
                $"Your deposit of {en} is held until you collect the car, when it counts towards the rental.",
                $"عربونك البالغ {ar} محتجز حتى تستلم السيارة، وعندها يُحتسب من قيمة الإيجار."),
            DepositStates.AppliedToRental => BilingualText.Of(
                $"Your deposit of {en} counts towards the rental.",
                $"يُحتسب عربونك البالغ {ar} من قيمة الإيجار."),
            DepositStates.InSettlementWindow => BilingualText.Of(
                $"Your deposit of {en} is held until {dateEn}, in case a dispute is opened.",
                $"عربونك البالغ {ar} محتجز حتى {dateAr} تحسّبًا لفتح نزاع."),
            DepositStates.UnderDispute => BilingualText.Of(
                $"Your deposit of {en} is held while the dispute is open.",
                $"عربونك البالغ {ar} محتجز ما دام النزاع مفتوحًا."),
            DepositStates.SettledWithRental => BilingualText.Of(
                $"Your deposit of {en} went towards the rental.",
                $"احتُسب عربونك البالغ {ar} من قيمة الإيجار."),
            DepositStates.ReturnedWithPayment => BilingualText.Of(
                "Your deposit is refunded with your payment.",
                "يُسترد عربونك مع دفعتك."),
            DepositStates.HeldUntilWindowCloses => BilingualText.Of(
                $"Your deposit of {en} is returned to you after {dateEn}, unless a dispute is opened before then.",
                $"يُعاد إليك عربونك البالغ {ar} بعد {dateAr}، ما لم يُفتح نزاع قبل ذلك."),
            DepositStates.HeldForAssessedPenalty => BilingualText.Of(
                $"Your deposit of {en} is held because a customer penalty was assessed. A dispute can be opened until {dateEn}.",
                $"عربونك البالغ {ar} محتجز لأنّ غرامةً قُدِّرت على العميل. يمكن فتح نزاع حتى {dateAr}."),
            DepositStates.HeldUnresolved => BilingualText.Of(
                "Your deposit remains held because a customer penalty was assessed and no dispute was opened. Final settlement is still pending.",
                "لا يزال عربونك محتجزًا لأنّ غرامةً قُدِّرت على العميل ولم يُفتح أيّ نزاع. التسوية النهائية لا تزال معلّقة."),
            // Payments Phase 8 (owner, 2026-09-29; pre-launch item 164): the amount and where it went. The owner's
            // explanation is the Penalty section's, and is never said twice (owner, 2026-09-30).
            DepositStates.KeptAsPenalty => BilingualText.Of(
                $"Your deposit of {en} was kept as the penalty assessed on this booking.",
                $"احتُفظ بعربونك البالغ {ar} بوصفه الغرامة المقدَّرة على هذا الحجز."),
            DepositStates.Released => BilingualText.Of(
                $"Your deposit of {en} was returned to you when the dispute window closed.",
                $"أُعيد إليك عربونك البالغ {ar} عند انتهاء مهلة النزاع."),
            DepositStates.DecidedByDispute when share is { IsZero: false } => BilingualText.Of(
                $"A dispute decided that {SnapshotJson.AmountText(share)} of your {en} deposit is refunded to you.",
                $"قرّر نزاع أن يُسترد لك {Isolate(SnapshotJson.AmountText(share))} من عربونك البالغ {ar}."),
            DepositStates.DecidedByDispute => BilingualText.Of(
                $"A dispute decided your {en} deposit; none of it is refunded to you.",
                $"قرّر نزاع مصير عربونك البالغ {ar}، ولا يُسترد لك منه شيء."),
            DepositStates.NotPaid => BilingualText.Of("No deposit has been paid.", "لم يُدفع أي عربون."),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "A deposit state with no words."),
        };
    }

    // ── The balance ────────────────────────────────────────────────────────────────────────────────

    public static readonly BilingualText PaidInFull = BilingualText.Of(
        "Paid in full online. Nothing is due to the rental office.",
        "دُفعت قيمة الحجز كاملة عبر الإنترنت. لا شيء مستحق لمكتب التأجير.");

    public static readonly BilingualText NothingFurtherDue = BilingualText.Of(
        "Nothing further is due on this booking.",
        "لا يترتب على هذا الحجز أي مبلغ آخر.");

    public static readonly BilingualText NothingDueYet = BilingualText.Of(
        "Nothing is due on this booking yet.",
        "لا شيء مستحق على هذا الحجز بعد.");

    // ── Composed values ────────────────────────────────────────────────────────────────────────────

    /// <summary>"3 days" / "3 أيام", with the Arabic plural forms the booking page uses.</summary>
    public static BilingualText Days(int days)
    {
        var digits = days.ToString(CultureInfo.InvariantCulture);
        var en = days == 1 ? "1 day" : $"{digits} days";
        var hundred = days % 100;
        var ar = days switch
        {
            1 => "يوم واحد",
            2 => "يومان",
            _ when hundred is >= 3 and <= 10 => $"{digits} أيام",
            _ when hundred is >= 11 and <= 99 => $"{digits} يومًا",
            _ => $"{digits} يوم",
        };
        return BilingualText.Of(en, ar);
    }

    /// <summary>"Rental: 3 days × 30.000 JOD".</summary>
    public static BilingualText Rental(int days, Money dailyRate)
    {
        var rate = SnapshotJson.AmountText(dailyRate);
        var count = Days(days);
        return BilingualText.Of($"Rental: {count.En} × {rate}", $"الإيجار: {count.Ar} × {Isolate(rate)}");
    }

    /// <summary>"Required deposit (20%)".</summary>
    public static BilingualText RequiredDepositPercent(decimal percent)
    {
        var text = percent.ToString("0.##", CultureInfo.InvariantCulture) + "%";
        return BilingualText.Of($"Required deposit ({text})", $"العربون المطلوب ({Isolate(text)})");
    }

    /// <summary>"BMW 525i 2002": the car as the rental office listed it.</summary>
    public static string Car(VehicleParty vehicle)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        return string.Create(CultureInfo.InvariantCulture, $"{vehicle.Make} {vehicle.Model} {vehicle.Year}");
    }

    /// <summary>
    /// Where the office is: its street, area and city, whichever it gave. What the office typed is shown as
    /// it typed it; the city is the platform's own list, in each language.
    /// </summary>
    public static BilingualText? Location(OfficeParty office)
    {
        ArgumentNullException.ThrowIfNull(office);
        string[] typed = [.. new[] { office.Street, office.Area }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim())];
        if (typed.Length == 0 && office.City is null)
            return null;

        var en = typed.Select(Isolate).ToList();
        var ar = typed.Select(Isolate).ToList();
        if (office.City is { } city)
        {
            en.Add(city.En);
            ar.Add(city.Ar);
        }

        return BilingualText.Of(string.Join(", ", en), string.Join("، ", ar));
    }

    /// <summary>
    /// Wraps a left-to-right run in first-strong isolates (U+2068 … U+2069) so an Arabic sentence around it
    /// keeps its order.
    /// </summary>
    public static string Isolate(string text) => $"⁨{text}⁩";
}
