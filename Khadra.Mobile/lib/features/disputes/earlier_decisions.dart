import '../../api/dtos.dart';
import '../../core/format/formats.dart';
import '../../l10n/app_localizations.dart';

/// What earlier disputes on a booking already decided (owner, 2026-09-26;
/// pre-launch item 169). Every figure is the SERVER's — the app never
/// subtracts; the only choice made here is which sentence, from whether the
/// server says anything is left for this ticket.

/// What earlier disputes decided, or null on a first dispute and on an API too
/// old to say.
Money? decidedEarlier(Dispute dispute) {
  final decided = dispute.decidedByEarlierTickets;
  return decided != null && decided.amount > 0 ? decided : null;
}

/// The notice on a LIVE ticket that earlier disputes already decided part or
/// all of the deposit, or null when none did. A closed ticket gets none: it is
/// not deciding anything now.
String? earlierDecisionNotice(
    Dispute dispute, AppLocalizations l10n, Formats formats) {
  final decided = decidedEarlier(dispute);
  final onBooking = dispute.depositOnBooking;
  if (!dispute.isLive || decided == null || onBooking == null) return null;
  return dispute.depositHeld.amount > 0
      ? l10n.disputeEarlierDecidedPart(formats.money(decided),
          formats.money(onBooking), formats.money(dispute.depositHeld))
      : l10n.disputeEarlierDecidedAll(formats.money(onBooking));
}
