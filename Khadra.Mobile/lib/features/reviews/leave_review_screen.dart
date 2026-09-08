import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../auth/auth_form_widgets.dart';
import '../bookings/booking_providers.dart';

/// Rating the rental office after a finished rental.
///
/// The SUBJECT is never named by the app — the server reads the gallery from the
/// booking, because a request that could name what it was rating could rate a
/// competitor. And the platform rates OFFICES, not cars: a renter comparing two
/// Corollas is choosing between two offices, which is what the spec models.
class LeaveReviewScreen extends ConsumerStatefulWidget {
  const LeaveReviewScreen({super.key, required this.bookingId});

  final String bookingId;

  @override
  ConsumerState<LeaveReviewScreen> createState() => _LeaveReviewScreenState();
}

class _LeaveReviewScreenState extends ConsumerState<LeaveReviewScreen> {
  final _comment = TextEditingController();
  int _rating = 0;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _comment.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final l10n = AppLocalizations.of(context);

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      await ref.read(apiProvider).leaveReview(
            bookingId: widget.bookingId,
            rating: _rating,
            comment: _comment.text.trim().isEmpty ? null : _comment.text.trim(),
          );

      invalidateBookings(ref, bookingId: widget.bookingId);

      if (!mounted) return;
      showKhadraMessage(context, l10n.reviewThanks);
      khadraLeave(context, Routes.bookings);
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      setState(() {
        _busy = false;
        _error = failure.messageFor(l10n);
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final booking = ref.watch(bookingProvider(widget.bookingId));
    final galleryName = booking.valueOrNull?.dealerName ?? '';

    return Scaffold(
      appBar: AppBar(title: Text(l10n.reviewTitle)),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(
            Space.lg, Space.xl, Space.lg, Space.bottomInset),
        children: [
          Text(
            galleryName.isEmpty
                ? l10n.reviewRatingLabel
                : l10n.reviewQuestion(galleryName),
            textAlign: TextAlign.center,
            style: const TextStyle(fontSize: 19, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: Space.sm),
          Text(
            l10n.reviewsRatingIsOfficeNote,
            textAlign: TextAlign.center,
            style: const TextStyle(
                color: KhadraColors.neutral600, fontSize: 13, height: 1.45),
          ),

          const SizedBox(height: Space.xl),
          Center(
            child: KhadraStars(
              rating: _rating == 0 ? null : _rating,
              size: 40,
              onChanged: _busy ? null : (value) => setState(() => _rating = value),
            ),
          ),
          if (_rating > 0) ...[
            const SizedBox(height: Space.sm),
            Center(
              child: Text(
                l10n.reviewStars(_rating),
                style: const TextStyle(
                    color: KhadraColors.neutral600, fontSize: 14),
              ),
            ),
          ],

          const SizedBox(height: Space.xl),
          KhadraField(
            controller: _comment,
            label: l10n.reviewCommentLabel,
            hint: l10n.reviewCommentHint,
            maxLines: 5,
            maxLength: 2000,
            enabled: !_busy,
          ),

          if (_error != null) ...[
            KhadraNotice(title: _error!, tone: NoticeTone.bad),
            const SizedBox(height: Space.lg),
          ],

          KhadraSubmitButton(
            label: l10n.reviewSubmit,
            busy: _busy,
            // A rating is required; a comment is not. The star is the thing that
            // feeds the office's score.
            onPressed: _rating == 0 ? null : _submit,
          ),
        ],
      ),
    );
  }
}
