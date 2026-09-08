import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/format/formats.dart';
import '../../core/providers.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import 'document_providers.dart';

/// A customer's identity paperwork.
///
/// The screen is honest about what happens to it: **nobody at Khadra checks these
/// in this version.** Nothing can move a document out of `PendingReview`, and the
/// rental office performs the legal check in person when the car is handed over.
/// A tile that read "verified" would be claiming a check the platform does not
/// make.
class DocumentsScreen extends ConsumerStatefulWidget {
  const DocumentsScreen({super.key});

  @override
  ConsumerState<DocumentsScreen> createState() => _DocumentsScreenState();
}

class _DocumentsScreenState extends ConsumerState<DocumentsScreen> {
  String? _uploading;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final documents = ref.watch(myDocumentsProvider);
    final formats = ref.watch(formatsProvider);

    return Scaffold(
      appBar: AppBar(title: Text(l10n.documentsTitle)),
      body: RefreshIndicator(
        onRefresh: () => ref.refresh(myDocumentsProvider.future),
        child: switch (documents) {
          AsyncLoading() => const KhadraLoading(),
          AsyncError(:final error) => KhadraError(
              message: ApiFailure.from(error).messageFor(l10n),
              onRetry: () => ref.invalidate(myDocumentsProvider),
            ),
          AsyncData(:final value) when formats != null =>
            _body(l10n, value, formats),
          _ => const KhadraLoading(),
        },
      ),
    );
  }

  Widget _body(
    AppLocalizations l10n,
    CustomerDocuments documents,
    Formats formats,
  ) {
    // What to show a tile for, entirely from the SERVER: what is still missing,
    // plus what has already been uploaded. Together those are exactly the documents
    // this customer needs.
    //
    // The app seeds nothing of its own. It used to add a "DrivingLicence" tile,
    // which does not exist — the platform asks for a FRONT and a BACK — so the
    // screen showed a fourth document nobody could ever satisfy. Which papers a
    // renter must file is the platform's rule (and differs for a foreign national,
    // who files a passport rather than a national ID); the app's job is to render
    // the answer, not to hold a copy of the question.
    final wanted = <String>{
      ...documents.missing,
      ...documents.documents.map((document) => document.type),
    };

    return ListView(
      padding: const EdgeInsets.fromLTRB(
          Space.lg, Space.lg, Space.lg, Space.bottomInset),
      children: [
        Text(
          l10n.documentsIntro,
          style: const TextStyle(fontSize: 14, height: 1.55),
        ),
        const SizedBox(height: Space.lg),

        KhadraNotice(
          title: documents.isComplete
              ? l10n.documentsComplete
              : l10n.documentsIncomplete,
          tone: documents.isComplete ? NoticeTone.accent : NoticeTone.warn,
        ),

        const SizedBox(height: Space.xl),
        for (final type in wanted) ...[
          _DocumentTile(
            type: type,
            document: documents.ofType(type),
            uploading: _uploading == type,
            formats: formats,
            onUpload: () => _upload(type),
            onView: () => _view(documents.ofType(type)!),
          ),
          const SizedBox(height: Space.md),
        ],

        const SizedBox(height: Space.lg),
        KhadraNotice(
          title: l10n.documentsNotYetCheckedTitle,
          body: l10n.documentsNotYetCheckedBody,
          tone: NoticeTone.neutral,
        ),
      ],
    );
  }

  Future<void> _upload(String type) async {
    final l10n = AppLocalizations.of(context);
    final limits = ref.read(appConfigProvider).valueOrNull?.documents;

    final source = await showModalBottomSheet<ImageSource>(
      context: context,
      builder: (_) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              leading: const Icon(Icons.photo_camera_outlined),
              title: Text(l10n.documentsTakePhoto),
              subtitle: Text(
                l10n.documentsCameraNote,
                style: const TextStyle(fontSize: 12),
              ),
              onTap: () => Navigator.of(context).pop(ImageSource.camera),
            ),
            ListTile(
              leading: const Icon(Icons.photo_library_outlined),
              title: Text(l10n.documentsChooseFile),
              onTap: () => Navigator.of(context).pop(ImageSource.gallery),
            ),
          ],
        ),
      ),
    );

    if (source == null) return;

    // JPEG is requested explicitly. An iPhone's camera writes HEIC by default,
    // and the platform's accepted types are JPEG, PNG, WebP and PDF -- so without
    // this every licence photo taken on an iPhone would be refused as an invalid
    // content type.
    final picked = await ImagePicker().pickImage(
      source: source,
      imageQuality: 88,
      maxWidth: 2400,
      requestFullMetadata: false,
    );

    if (picked == null || !mounted) return;

    final bytes = await picked.readAsBytes();

    // Checked here as well as on the server, because the failure is expensive: a
    // customer on a Jordanian mobile network should not upload eight megabytes to
    // be told it was one too many.
    if (limits != null && bytes.length > limits.maximumSizeBytes) {
      if (!mounted) return;
      showKhadraMessage(
        context,
        l10n.documentsTooLarge(_megabytes(limits.maximumSizeBytes)),
        isError: true,
      );
      return;
    }

    setState(() => _uploading = type);

    try {
      await ref.read(apiProvider).uploadDocument(
            type: type,
            bytes: bytes,
            fileName: picked.name.toLowerCase().endsWith('.jpg') ||
                    picked.name.toLowerCase().endsWith('.jpeg')
                ? picked.name
                : '${picked.name}.jpg',
            contentType: 'image/jpeg',
          );

      ref.invalidate(myDocumentsProvider);
      if (!mounted) return;
      setState(() => _uploading = null);
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      setState(() => _uploading = null);
      showKhadraMessage(context, failure.messageFor(l10n), isError: true);
    }
  }

  /// Opens a short-lived signed link.
  ///
  /// The API never returns a URL on the listing, deliberately: these files are
  /// passports and licences, and a permanent address for one would be a public URL
  /// wearing a disguise. The link is minted per view and expires.
  Future<void> _view(CustomerDocument document) async {
    final l10n = AppLocalizations.of(context);
    try {
      final link = await ref.read(apiProvider).documentLink(document.documentId);
      await launchUrl(Uri.parse(link.url), mode: LaunchMode.externalApplication);
    } on ApiFailure catch (failure) {
      if (mounted) {
        showKhadraMessage(context, failure.messageFor(l10n), isError: true);
      }
    }
  }

  static String _megabytes(int bytes) =>
      '${(bytes / (1024 * 1024)).toStringAsFixed(0)} MB';
}

class _DocumentTile extends StatelessWidget {
  const _DocumentTile({
    required this.type,
    required this.document,
    required this.uploading,
    required this.formats,
    required this.onUpload,
    required this.onView,
  });

  final String type;
  final CustomerDocument? document;
  final bool uploading;
  final Formats formats;
  final VoidCallback onUpload;
  final VoidCallback onView;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final present = document != null;

    return KhadraCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(
                _icon(type),
                color: present ? KhadraColors.accent : KhadraColors.neutral400,
              ),
              const SizedBox(width: Space.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      _label(l10n, type),
                      style: const TextStyle(
                          fontSize: 15, fontWeight: FontWeight.w600),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      present
                          ? l10n.documentsUploaded(
                              formats.longDate(document!.uploadedAt))
                          : l10n.documentsMissing,
                      style: TextStyle(
                        fontSize: 12,
                        color: present
                            ? KhadraColors.neutral600
                            : KhadraColors.warn,
                      ),
                    ),
                  ],
                ),
              ),
              if (present)
                KhadraBadge(
                  label: _statusLabel(l10n, document!.status),
                  colour: _statusColour(document!.status),
                ),
            ],
          ),
          if (document?.reviewNote != null &&
              document!.reviewNote!.isNotEmpty) ...[
            const SizedBox(height: Space.sm),
            Text(
              document!.reviewNote!,
              style: const TextStyle(
                  fontSize: 13, height: 1.45, color: KhadraColors.bad),
            ),
          ],
          const SizedBox(height: Space.md),
          Row(
            children: [
              Expanded(
                child: uploading
                    ? const Center(
                        child: SizedBox(
                          width: 20,
                          height: 20,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        ),
                      )
                    : OutlinedButton.icon(
                        onPressed: onUpload,
                        icon: const Icon(Icons.upload_outlined, size: 18),
                        label: Text(present
                            ? l10n.documentsReplace
                            : l10n.documentsUpload),
                      ),
              ),
              if (present && !uploading) ...[
                const SizedBox(width: Space.sm),
                Expanded(
                  child: OutlinedButton.icon(
                    onPressed: onView,
                    icon: const Icon(Icons.visibility_outlined, size: 18),
                    label: Text(l10n.documentsView),
                  ),
                ),
              ],
            ],
          ),
        ],
      ),
    );
  }

  static IconData _icon(String type) => switch (type) {
        DocumentTypes.drivingLicenceFront ||
        DocumentTypes.drivingLicenceBack =>
          Icons.credit_card_outlined,
        DocumentTypes.passport => Icons.book_outlined,
        _ => Icons.badge_outlined,
      };

  static String _label(AppLocalizations l10n, String type) => switch (type) {
        DocumentTypes.drivingLicenceFront => l10n.documentsDrivingLicenceFront,
        DocumentTypes.drivingLicenceBack => l10n.documentsDrivingLicenceBack,
        DocumentTypes.nationalId => l10n.documentsNationalId,
        DocumentTypes.passport => l10n.documentsPassport,
        // A type this build has never heard of still gets a tile, named by the
        // platform's own word for it, rather than disappearing. Ugly, and better
        // than a document a customer is required to file and never shown.
        _ => type,
      };

  static String _statusLabel(AppLocalizations l10n, String status) =>
      switch (status) {
        'PendingReview' => l10n.documentsStatusPendingReview,
        'Verified' => l10n.documentsStatusVerified,
        'Rejected' => l10n.documentsStatusRejected,
        _ => status,
      };

  static Color _statusColour(String status) => switch (status) {
        'Verified' => KhadraColors.accent,
        'Rejected' => KhadraColors.bad,
        _ => KhadraColors.neutral600,
      };
}
