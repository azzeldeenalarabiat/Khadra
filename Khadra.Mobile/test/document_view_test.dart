import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/api/api_failure_messages.dart';
import 'package:khadra_mobile/core/uploads/document_viewer.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';

/// Reading back a document you uploaded, from the app rather than from a browser
/// (pre-launch item 93).
///
/// The download endpoint wants a signed link AND a live session. An external
/// browser has the first and can never have the second, so `View` used to hand
/// Chrome a URL and the customer got a page of JSON saying 401. The app fetches
/// the bytes itself now, over the same authenticated connection as everything
/// else, and writes them somewhere private before asking the platform to open
/// them.
///
/// What is worth pinning here is the part that can fail on a real phone and must
/// not look like a crash: no bytes, no viewer, a cache that cannot be written.
void main() {
  group('the bytes the viewer is handed', () {
    test('an empty body is a failure to open, not a blank viewer', () async {
      // A zero-length response is not success with nothing in it. Opening an
      // empty file shows a viewer with no content, which is indistinguishable
      // from a document that failed to upload -- so it is reported as a failure
      // and the screen says so in words.
      final opened = await DocumentViewer.open(
        bytes: Uint8List(0),
        contentType: 'application/pdf',
        documentId: 'doc-1',
      );

      expect(opened, isFalse);
    });
  });

  group('what the server said it is', () {
    // The platform opens by EXTENSION as much as by MIME type, so a file written
    // without one, or with the wrong one, is a document the phone declines to
    // open for a reason nobody can see. The mapping is the server's word for the
    // bytes, not a guess from the file name.
    for (final (contentType, extension) in <(String?, String)>[
      ('application/pdf', '.pdf'),
      ('image/jpeg', '.jpg'),
      ('image/png', '.png'),
      ('image/webp', '.webp'),
      (null, '.bin'),
      ('application/octet-stream', '.bin'),
    ]) {
      test('$contentType is written as $extension', () {
        expect(DocumentViewer.extensionFor(contentType), extension);
      });
    }
  });

  group('the screen when it goes wrong', () {
    testWidgets('a refused download shows the server\'s words, not a crash',
        (tester) async {
      // ApiFailure is what the authenticated fetch throws when the download is
      // refused -- an expired link, a revoked session. The screen has to say
      // something a person can act on.
      const failure = ApiFailure(
        kind: ApiFailureKind.notFound,
        code: 'documents.not_found',
        statusCode: 404,
      );

      late String shown;
      await tester.pumpWidget(_Harness(
        onBuild: (l10n) => shown = failure.messageFor(l10n),
      ));
      await tester.pump();

      expect(shown, isNotEmpty);
      // Not a Dart error string leaking to the customer.
      expect(shown, isNot(contains('Exception')));
      expect(shown, isNot(contains('DioError')));
    });

    testWidgets('the open-failed message exists in both languages',
        (tester) async {
      // The one message that only appears on a device with nothing able to open
      // a PDF. It is easy to add to one ARB and forget the other, and the
      // failure would only be visible to an Arabic reader on such a phone.
      for (final locale in const [Locale('en'), Locale('ar')]) {
        late String message;
        await tester.pumpWidget(_Harness(
          locale: locale,
          onBuild: (l10n) => message = l10n.documentsOpenFailed,
        ));
        await tester.pump();

        expect(message, isNotEmpty, reason: '$locale');
        expect(message.trim(), message, reason: '$locale');
      }
    });
  });

  group('what a signed link is worth to this app', () {
    test('the link the server mints is absolute by the time it is fetched', () {
      // `SignedDocumentLink.fromJson` resolves the server's path against the API
      // base, because the fetch goes through Dio with an absolute URL. A relative
      // one would be resolved against the base a second time and 404.
      final link = SignedDocumentLink.fromJson(const <String, dynamic>{
        'url': '/api/v1/documents/abc?expires=1&signature=x',
        'expiresAt': null,
      });

      expect(Uri.parse(link.url).isAbsolute, isTrue);
      expect(link.url, contains('/api/v1/documents/abc'));
    });
  });
}

/// The smallest thing that can hand a test a real `AppLocalizations`.
class _Harness extends StatelessWidget {
  const _Harness({required this.onBuild, this.locale = const Locale('en')});

  final void Function(AppLocalizations) onBuild;
  final Locale locale;

  @override
  Widget build(BuildContext context) => ProviderScope(
        child: MaterialApp(
          locale: locale,
          localizationsDelegates: const [
            AppLocalizations.delegate,
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          supportedLocales: AppLocalizations.supportedLocales,
          home: Builder(builder: (context) {
            onBuild(AppLocalizations.of(context));
            return const SizedBox.shrink();
          }),
        ),
      );
}
