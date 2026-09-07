import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:image_picker/image_picker.dart';

import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../auth/auth_form_widgets.dart';
import '../bookings/booking_providers.dart';

/// Opening a dispute (spec 3.3).
///
/// Evidence is a TWO-STEP upload: ask the server where to put the file, PUT the
/// bytes there, then name the returned key when opening the ticket. The app never
/// invents a storage key, and never holds a permanent URL to one — a signed URL is
/// a credential, and these are photographs of somebody's rental.
class OpenDisputeScreen extends ConsumerStatefulWidget {
  const OpenDisputeScreen({super.key, required this.bookingId});

  final String bookingId;

  @override
  ConsumerState<OpenDisputeScreen> createState() => _OpenDisputeScreenState();
}

class _OpenDisputeScreenState extends ConsumerState<OpenDisputeScreen> {
  final _reason = TextEditingController();
  final _evidenceKeys = <String>[];
  final _evidenceNames = <String>[];

  bool _busy = false;
  bool _uploading = false;
  String? _error;

  @override
  void dispose() {
    _reason.dispose();
    super.dispose();
  }

  Future<void> _addEvidence() async {
    final l10n = AppLocalizations.of(context);

    final picked = await ImagePicker().pickImage(
      source: ImageSource.gallery,
      imageQuality: 85,
      maxWidth: 2000,
      requestFullMetadata: false,
    );
    if (picked == null || !mounted) return;

    setState(() => _uploading = true);

    try {
      final bytes = await picked.readAsBytes();
      final fileName = picked.name.toLowerCase().endsWith('.jpg') ||
              picked.name.toLowerCase().endsWith('.jpeg')
          ? picked.name
          // JPEG explicitly: an iPhone writes HEIC, which the platform does not
          // accept.
          : '${picked.name}.jpg';

      final upload = await ref.read(apiProvider).requestEvidenceUpload(
            bookingId: widget.bookingId,
            fileName: fileName,
            contentType: 'image/jpeg',
          );

      await ref.read(apiProvider).uploadEvidence(
            uploadUrl: upload.uploadUrl,
            bytes: bytes,
            contentType: 'image/jpeg',
          );

      if (!mounted) return;
      setState(() {
        _uploading = false;
        _evidenceKeys.add(upload.storageKey);
        _evidenceNames.add(fileName);
      });
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      setState(() => _uploading = false);
      showKhadraMessage(context, failure.messageFor(l10n), isError: true);
    }
  }

  Future<void> _submit() async {
    final l10n = AppLocalizations.of(context);

    if (_reason.text.trim().isEmpty) {
      setState(() => _error = l10n.validationRequired);
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      final dispute = await ref.read(apiProvider).openDispute(
            bookingId: widget.bookingId,
            reason: _reason.text.trim(),
            evidenceKeys: _evidenceKeys,
          );

      invalidateBookings(ref, bookingId: widget.bookingId);

      if (!mounted) return;
      showKhadraMessage(
        context,
        l10n.disputeOpened(
          dispute.slaDeadline.difference(dispute.openedAt).inHours.toString(),
        ),
      );
      context.pushReplacement(Routes.dispute(dispute.ticketId));
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

    return Scaffold(
      appBar: AppBar(title: Text(l10n.disputeTitle)),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(
            Space.lg, Space.lg, Space.lg, Space.bottomInset),
        children: [
          Text(
            l10n.disputeBody,
            style: const TextStyle(fontSize: 14, height: 1.55),
          ),
          const SizedBox(height: Space.xl),

          KhadraField(
            controller: _reason,
            label: l10n.disputeReason,
            maxLines: 6,
            maxLength: 2000,
            enabled: !_busy,
          ),

          KhadraSectionTitle(l10n.disputeEvidence),
          if (_evidenceNames.isNotEmpty) ...[
            for (var index = 0; index < _evidenceNames.length; index++)
              ListTile(
                dense: true,
                contentPadding: EdgeInsets.zero,
                leading: const Icon(Icons.image_outlined,
                    color: KhadraColors.neutral600),
                title: Text(
                  _evidenceNames[index],
                  style: const TextStyle(fontSize: 13),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
                trailing: IconButton(
                  icon: const Icon(Icons.close, size: 18),
                  onPressed: _busy
                      ? null
                      : () => setState(() {
                            _evidenceKeys.removeAt(index);
                            _evidenceNames.removeAt(index);
                          }),
                ),
              ),
            const SizedBox(height: Space.sm),
          ],
          OutlinedButton.icon(
            onPressed: _busy || _uploading ? null : _addEvidence,
            icon: _uploading
                ? const SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.add_photo_alternate_outlined, size: 18),
            label: Text(l10n.disputeAddEvidence),
          ),

          const SizedBox(height: Space.xl),
          if (_error != null) ...[
            KhadraNotice(title: _error!, tone: NoticeTone.bad),
            const SizedBox(height: Space.lg),
          ],
          KhadraSubmitButton(
            label: l10n.disputeOpen,
            busy: _busy,
            onPressed: _submit,
          ),
        ],
      ),
    );
  }
}
