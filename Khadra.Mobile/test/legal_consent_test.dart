import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/config/update_requirement.dart';
import 'package:khadra_mobile/core/live/live_refresh.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/router.dart';
import 'package:khadra_mobile/core/theme/khadra_theme.dart';
import 'package:khadra_mobile/features/auth/register_screen.dart';
import 'package:khadra_mobile/features/legal/consent_prompt_screen.dart';
import 'package:khadra_mobile/features/legal/legal_text_screen.dart';
import 'package:khadra_mobile/features/profile/profile_screen.dart';
import 'package:khadra_mobile/features/shell/welcome_screen.dart';
import 'package:khadra_mobile/features/update/update_required_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:khadra_mobile/main.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fake_api.dart';
import 'support/fake_push_messaging.dart';

/// Consent to the legal texts in the app, which arrives in 1.4.0 (pre-launch items 224 and 238).
///
/// From 1.4.0 the API holds this app to the texts in force: a registration must accept them,
/// and a signed-in person with one still to accept is refused (`403 legal.consent_pending`)
/// everywhere except the few calls that resolve it. So the app asks at registration, puts a
/// prompt in front of everything when one is pending, and shows each text in the version
/// being accepted — never a cached older one.
void main() {
  final en = lookupAppLocalizations(const Locale('en'));

  Map<String, dynamic> documentJson(String kind, String versionId, {String label = '2026-10', bool urls = true}) => {
        'kind': kind,
        'slug': kind.toLowerCase(),
        'versionId': versionId,
        'versionLabel': label,
        'effectiveFrom': '2026-10-01T00:00:00Z',
        'pageUrls': urls
            ? {
                'en': 'https://khadra.test/en/legal/${kind.toLowerCase()}',
                'ar': 'https://khadra.test/ar/legal/${kind.toLowerCase()}',
              }
            : null,
      };
  final terms = LegalDocumentRef.maybe(documentJson('Terms', 'v-terms-3'))!;
  final privacy = LegalDocumentRef.maybe(documentJson('Privacy', 'v-privacy-2'))!;
  Map<String, dynamic> legalBlock(List<Map<String, dynamic>> documents) => {'documents': documents};

  PublicLegalDocument publicText(LegalDocumentRef of, {String? versionId}) => PublicLegalDocument(
        kind: of.kind,
        versionId: versionId ?? of.versionId,
        versionLabel: of.versionLabel,
        htmlEn: '<h1>${of.kind}</h1><p>English text.</p>',
        htmlAr: '<h1>${of.kind}</h1><p>النص العربي.</p>',
      );

  const conflict = ApiFailure(kind: ApiFailureKind.conflict, code: 'legal.version_not_current', statusCode: 409);

  // ── What the server sends ────────────────────────────────────────────────────────────────────

  group('the contract', () {
    test('a config that could not read its texts is "not known", never "nothing published"', () {
      expect(FakeApi.fakeConfig().legal, isNull);
      expect(FakeApi.fakeConfig(legal: legalBlock(const [])).legal!.documents, isEmpty);
    });

    test('reads each text in force, with its pages, and drops one it cannot name', () {
      final legal = FakeApi.fakeConfig(
        legal: legalBlock([
          documentJson('Terms', 'v-terms-3'),
          documentJson('Privacy', 'v-privacy-2', urls: false),
          {'kind': 'Terms', 'slug': 'terms', 'versionLabel': 'no id'},
        ]),
      ).legal!;

      expect(legal.documents.map((d) => d.versionId), ['v-terms-3', 'v-privacy-2']);
      expect(legal.documents.first.isTerms, isTrue);
      expect(legal.documents.first.pageUrls!.forLanguage(arabic: true), 'https://khadra.test/ar/legal/terms');
      expect(legal.documents.last.pageUrls, isNull, reason: 'no link is invented while the site address is unset');
    });

    test('a page address in only one language is no address at all', () {
      expect(LegalPageUrls.maybe({'en': 'https://khadra.test/en/legal/terms'}), isNull);
    });

    test("/auth/me names what is still to accept, and an older API's answer names nothing", () {
      Map<String, dynamic> me([List<Map<String, dynamic>>? pending]) => {
            'id': 'u-1',
            'email': 'layla@example.jo',
            'fullName': 'Layla Odeh',
            'role': 'Customer',
            'createdAt': '2026-01-05T00:00:00Z',
            if (pending != null) 'pendingConsents': pending,
          };

      expect(AuthUser.fromJson(me([documentJson('Terms', 'v-terms-3')])).pendingConsents.single.versionId, 'v-terms-3');
      expect(AuthUser.fromJson(me()).pendingConsents, isEmpty);
    });

    test('the public read carries both languages of the version it is', () {
      final text = PublicLegalDocument.fromJson({
        'kind': 'Terms',
        'versionId': 'v-terms-3',
        'versionLabel': '2026-10',
        'effectiveFrom': '2026-10-01T00:00:00Z',
        'publishedAt': '2026-09-30T12:00:00Z',
        'html': {'en': '<p>Hello</p>', 'ar': '<p>مرحبا</p>'},
      });

      expect(text.versionId, 'v-terms-3');
      expect(text.htmlFor(arabic: false), '<p>Hello</p>');
      expect(text.htmlFor(arabic: true), '<p>مرحبا</p>');
    });

    test('a link inside a text may leave the app only for https: or mailto:', () {
      expect(legalLinkOpensOutside(Uri.parse('https://khadra.test/en/legal/privacy')), isTrue);
      expect(legalLinkOpensOutside(Uri.parse('mailto:privacy@khadra.test')), isTrue);
      for (final refused in ['http://khadra.test', 'javascript:alert(1)', 'intent://x', 'file:///etc/hosts', 'tel:123']) {
        expect(legalLinkOpensOutside(Uri.parse(refused)), isFalse, reason: refused);
      }
    });
  });

  // ── A screen inside the app's providers ───────────────────────────────────────────────────────

  Future<ProviderContainer> pumpScreen(
    WidgetTester tester,
    FakeApi api,
    Widget screen, {
    Locale locale = const Locale('en'),
    bool signedIn = true,
  }) async {
    tester.view.physicalSize = const Size(412, 1400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    final container = ProviderContainer(overrides: [
      apiProvider.overrideWithValue(api),
      sessionStoreProvider.overrideWithValue(FakeSessionStore()),
      sharedPreferencesProvider.overrideWithValue(null),
      isArabicProvider.overrideWithValue(locale.languageCode == 'ar'),
    ]);
    addTearDown(container.dispose);
    if (signedIn) await container.read(sessionProvider.notifier).adoptTokens(FakeApi.fakeTokens());
    await container.read(appConfigProvider.future);

    await tester.pumpWidget(UncontrolledProviderScope(
      container: container,
      child: MaterialApp(
        locale: locale,
        theme: KhadraTheme.light(),
        supportedLocales: AppLocalizations.supportedLocales,
        localizationsDelegates: const [
          AppLocalizations.delegate,
          GlobalMaterialLocalizations.delegate,
          GlobalWidgetsLocalizations.delegate,
          GlobalCupertinoLocalizations.delegate,
        ],
        home: screen,
      ),
    ));
    await tester.pumpAndSettle();
    return container;
  }

  /// What a test's stand-in for the WebView was handed.
  final drawn = <({String html, bool arabic, Uri? base})>[];
  Widget htmlView(BuildContext context, String html, {required bool arabic, Uri? base}) {
    drawn.add((html: html, arabic: arabic, base: base));
    return const Text('the rendered text', key: ValueKey('legal-html'));
  }

  setUp(drawn.clear);

  group('a legal text', () {
    testWidgets('is drawn exactly as the server rendered it, in the version being accepted', (tester) async {
      final api = FakeApi()..legalTexts = {'terms': publicText(terms)};
      await pumpScreen(tester, api, LegalTextScreen(document: terms, htmlView: htmlView));

      expect(api.legalTextReads, ['terms']);
      expect(find.byKey(const ValueKey('legal-html')), findsOneWidget);
      expect(find.text(en.legalVersion('2026-10')), findsOneWidget);
      expect(drawn.last.html, '<h1>Terms</h1><p>English text.</p>');
      expect(drawn.last.arabic, isFalse);
      expect(drawn.last.base, Uri.parse('https://khadra.test/en/legal/terms'));
    });

    testWidgets('in Arabic, the Arabic text under the Arabic page', (tester) async {
      final api = FakeApi()..legalTexts = {'terms': publicText(terms)};
      await pumpScreen(tester, api, LegalTextScreen(document: terms, htmlView: htmlView), locale: const Locale('ar'));

      expect(drawn.last.html, '<h1>Terms</h1><p>النص العربي.</p>');
      expect(drawn.last.arabic, isTrue);
      expect(drawn.last.base, Uri.parse('https://khadra.test/ar/legal/terms'));
    });

    testWidgets('a cached copy of another version is never shown: it says the text was just updated', (tester) async {
      final api = FakeApi()..legalTexts = {'terms': publicText(terms, versionId: 'v-terms-2')};
      await pumpScreen(tester, api, LegalTextScreen(document: terms, htmlView: htmlView));

      expect(drawn, isEmpty);
      expect(find.text(en.legalTextJustUpdated), findsOneWidget);

      // Once the cache has caught up, Retry reads the version being accepted.
      api.legalTexts = {'terms': publicText(terms)};
      await tester.tap(find.text(en.actionRetry));
      await tester.pumpAndSettle();
      expect(api.legalTextReads, ['terms', 'terms']);
      expect(find.byKey(const ValueKey('legal-html')), findsOneWidget);
    });

    testWidgets('a text that cannot be read says so, and offers to try again', (tester) async {
      await pumpScreen(tester, FakeApi(), LegalTextScreen(document: terms, htmlView: htmlView));

      expect(drawn, isEmpty);
      expect(find.text(en.actionRetry), findsOneWidget);
    });
  });

  // E2E F7: the texts are reachable from Profile at any time, each in the version in force.
  group('Profile', () {
    testWidgets('lists each text /app-config names, and opens it', (tester) async {
      final api = FakeApi()
        ..legal = legalBlock([documentJson('Terms', 'v-terms-3'), documentJson('Privacy', 'v-privacy-2')]);
      // No public text is staged: the screen opens and reads it, and stops short of the
      // WebView a test has not got.
      await pumpScreen(tester, api, const ProfileScreen());

      final row = find.text(en.legalPrivacy);
      await tester.scrollUntilVisible(row, 200, scrollable: find.byType(Scrollable).first);
      expect(find.text(en.legalTerms), findsOneWidget);

      await tester.tap(row);
      await tester.pump();
      await tester.pump();
      expect(find.byType(LegalTextScreen), findsOneWidget);
      expect(api.legalTextReads, ['privacy']);
    });

    testWidgets('invents no link when the config could not say which texts are in force', (tester) async {
      await pumpScreen(tester, FakeApi(), const ProfileScreen());

      expect(find.text(en.legalTerms, skipOffstage: false), findsNothing);
      expect(find.text(en.legalPrivacy, skipOffstage: false), findsNothing);
    });
  });

  group('the consent prompt', () {
    late int accepted;

    Future<FakeApi> prompt(WidgetTester tester, {List<LegalDocumentRef>? pending, Locale locale = const Locale('en')}) async {
      accepted = 0;
      final api = FakeApi()
        ..pendingConsents = pending ?? [terms, privacy]
        ..legalTexts = {'terms': publicText(terms), 'privacy': publicText(privacy)};
      await pumpScreen(
        tester,
        api,
        ConsentPromptScreen(onAccepted: () => accepted++, htmlView: htmlView),
        locale: locale,
      );
      return api;
    }

    Future<void> tick(WidgetTester tester) async {
      await tester.tap(find.byKey(const ValueKey('consent-agree')));
      await tester.pump();
    }

    Future<void> accept(WidgetTester tester) async {
      await tester.tap(find.byKey(const ValueKey('consent-accept')));
      await tester.pumpAndSettle();
    }

    testWidgets('names each text still to accept, in the version in force now', (tester) async {
      final api = await prompt(tester);

      expect(api.legalConsentReads, 1, reason: 'read uncached from /auth/me/legal-consents, not /app-config');
      expect(find.text(en.consentTitle), findsOneWidget);
      expect(find.text(en.legalTerms), findsOneWidget);
      expect(find.text(en.legalPrivacy), findsOneWidget);
      expect(find.byKey(const ValueKey('consent-read-Terms')), findsOneWidget);
      expect(find.byKey(const ValueKey('consent-read-Privacy')), findsOneWidget);
      expect(find.byKey(const ValueKey('consent-sign-out')), findsOneWidget);
      expect(tester.widget<CheckboxListTile>(find.byKey(const ValueKey('consent-agree'))).value, isFalse,
          reason: 'never pre-ticked');
    });

    testWidgets('accepts nothing until the box is ticked', (tester) async {
      final api = await prompt(tester);

      await accept(tester);

      expect(api.acceptances, isEmpty);
      expect(find.text(en.consentRequired), findsOneWidget);
      expect(accepted, 0);
    });

    testWidgets('accepts exactly the versions it showed, in the language they were read in', (tester) async {
      final api = await prompt(tester, locale: const Locale('ar'));

      await tick(tester);
      await accept(tester);

      expect(api.acceptances.single.$1, ['v-terms-3', 'v-privacy-2']);
      expect(api.acceptances.single.$2, 'ar');
      expect(accepted, 1);
    });

    testWidgets('opens each text from its card', (tester) async {
      await prompt(tester);

      await tester.tap(find.byKey(const ValueKey('consent-read-Privacy')));
      await tester.pumpAndSettle();

      expect(find.byType(LegalTextScreen), findsOneWidget);
      expect(drawn.last.html, '<h1>Privacy</h1><p>English text.</p>');
    });

    testWidgets('steps aside at once when nothing is pending any more (accepted on the website)', (tester) async {
      await prompt(tester, pending: const []);

      expect(accepted, 1);
    });

    testWidgets('a version that changed under the person is read again and asked for again', (tester) async {
      final api = await prompt(tester);
      final newer = LegalDocumentRef.maybe(documentJson('Terms', 'v-terms-4', label: '2026-11'))!;
      api.acceptanceRefusals.add(conflict);

      await tick(tester);
      api.pendingConsents = [newer, privacy];
      await accept(tester);

      expect(api.legalConsentReads, 2);
      expect(find.text(en.consentChanged), findsOneWidget);
      expect(find.text(en.legalVersion('2026-11')), findsOneWidget);
      expect(tester.widget<CheckboxListTile>(find.byKey(const ValueKey('consent-agree'))).value, isFalse,
          reason: 'a tick given to the old version does not carry over');

      await tick(tester);
      await accept(tester);
      expect(api.acceptances.last.$1, ['v-terms-4', 'v-privacy-2']);
      expect(accepted, 1);
    });

    testWidgets('stops after ${ConsentPromptScreen.maxReloads} changes rather than spin, and says so', (tester) async {
      final api = await prompt(tester);
      api.acceptanceRefusals.addAll(List.filled(ConsentPromptScreen.maxReloads + 1, conflict));

      for (var i = 0; i <= ConsentPromptScreen.maxReloads; i++) {
        await tick(tester);
        await accept(tester);
      }

      expect(api.legalConsentReads, 1 + ConsentPromptScreen.maxReloads);
      expect(find.text(en.consentFailed), findsOneWidget);
      expect(accepted, 0);
    });

    testWidgets('a text that came into force between the read and the acceptance is asked for too', (tester) async {
      final api = await prompt(tester, pending: [terms]);
      api.pendingAfterAcceptance = [privacy];

      await tick(tester);
      await accept(tester);

      expect(accepted, 0);
      expect(find.text(en.consentChanged), findsOneWidget);
      expect(find.text(en.legalPrivacy), findsOneWidget);
      expect(find.text(en.legalTerms), findsNothing);
    });
  });

  group('registration', () {
    // The real faces, so the form is laid out at the widths a phone draws (the test font's
    // square glyphs push the form's footer row past the edge).
    setUpAll(() async {
      for (final family in <String, List<String>>{
        'Manrope': ['Manrope-400.ttf', 'Manrope-500.ttf', 'Manrope-600.ttf', 'Manrope-700.ttf', 'Manrope-800.ttf'],
        'Noto Kufi Arabic': [
          'NotoKufiArabic-400.ttf',
          'NotoKufiArabic-500.ttf',
          'NotoKufiArabic-600.ttf',
          'NotoKufiArabic-700.ttf',
        ],
      }.entries) {
        final loader = FontLoader(family.key);
        for (final file in family.value) {
          loader.addFont(Future<ByteData>.value(ByteData.sublistView(File('assets/fonts/$file').readAsBytesSync())));
        }
        await loader.load();
      }
    });

    final router = GoRouter(
      initialLocation: '/register',
      routes: [
        GoRoute(path: '/register', builder: (_, __) => const RegisterScreen()),
        GoRoute(path: Routes.verifyEmail, builder: (_, __) => const Text('check your inbox')),
      ],
    );

    Future<FakeApi> open(WidgetTester tester, {Map<String, dynamic>? legal}) async {
      tester.view.physicalSize = const Size(412, 2000);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      router.go('/register');

      final api = FakeApi()..legal = legal;
      final container = ProviderContainer(overrides: [
        apiProvider.overrideWithValue(api),
        sessionStoreProvider.overrideWithValue(FakeSessionStore()),
        sharedPreferencesProvider.overrideWithValue(null),
      ]);
      addTearDown(container.dispose);
      await container.read(appConfigProvider.future);

      await tester.pumpWidget(UncontrolledProviderScope(
        container: container,
        child: MaterialApp.router(
          routerConfig: router,
          theme: KhadraTheme.light(),
          supportedLocales: AppLocalizations.supportedLocales,
          localizationsDelegates: const [
            AppLocalizations.delegate,
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
        ),
      ));
      await tester.pumpAndSettle();

      final fields = find.byType(TextFormField);
      await tester.enterText(fields.at(0), 'Layla Odeh');
      await tester.enterText(fields.at(1), 'layla@example.jo');
      await tester.enterText(fields.at(2), '0791234567');
      await tester.enterText(fields.at(3), 'a long enough passphrase 42');
      await tester.pump();
      return api;
    }

    Future<void> submit(WidgetTester tester) async {
      final button = find.text(en.authSignUp);
      await tester.ensureVisible(button);
      await tester.tap(button);
      await tester.pumpAndSettle();
    }

    final inForce = legalBlock([documentJson('Terms', 'v-terms-3'), documentJson('Privacy', 'v-privacy-2')]);

    testWidgets('asks for the texts in force, unticked, and sends nothing until they are accepted', (tester) async {
      final api = await open(tester, legal: inForce);

      final box = find.byKey(const ValueKey('register-consent'));
      expect(tester.widget<Checkbox>(box).value, isFalse);
      expect(find.textContaining(en.legalTerms, findRichText: true), findsOneWidget);

      await submit(tester);
      expect(api.registrations, isEmpty);
      expect(find.text(en.consentRequired), findsOneWidget);
    });

    testWidgets('sends exactly the versions /app-config listed, and the language they were read in', (tester) async {
      final api = await open(tester, legal: inForce);

      await tester.ensureVisible(find.byKey(const ValueKey('register-consent')));
      await tester.tap(find.byKey(const ValueKey('register-consent')));
      await submit(tester);

      expect(api.registrations.single.$2, ['v-terms-3', 'v-privacy-2']);
      expect(api.registrations.single.$3, 'en');
      expect(find.text('check your inbox'), findsOneWidget);
    });

    testWidgets('a version that changed while the form was open is read again, and the box untick', (tester) async {
      final api = await open(tester, legal: inForce);
      api.registrationRefusals.add(conflict);

      await tester.ensureVisible(find.byKey(const ValueKey('register-consent')));
      await tester.tap(find.byKey(const ValueKey('register-consent')));
      await submit(tester);

      expect(find.text(en.consentVersionChanged), findsOneWidget);
      expect(tester.widget<Checkbox>(find.byKey(const ValueKey('register-consent'))).value, isFalse);
    });

    testWidgets('with nothing in force there is nothing to tick', (tester) async {
      final api = await open(tester, legal: legalBlock(const []));

      expect(find.byKey(const ValueKey('register-consent')), findsNothing);
      await submit(tester);
      expect(api.registrations.single.$2, isEmpty);
    });

    testWidgets('texts the server could not name are fetched again rather than sent as none', (tester) async {
      final api = await open(tester);
      api.registrationRefusals.add(
          const ApiFailure(kind: ApiFailureKind.validation, code: 'legal.consent_required', statusCode: 400));

      await submit(tester);

      expect(find.text(en.consentTextsUnavailable), findsOneWidget);
    });
  });

  group('the whole app, at the root', () {
    Future<(ProviderContainer, _RestoringApi)> launch(WidgetTester tester, {List<LegalDocumentRef> pending = const []}) async {
      tester.view.physicalSize = const Size(1000, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      SharedPreferences.setMockInitialValues({'khadra.entry_chosen': true});

      final api = _RestoringApi()
        ..pendingConsents = pending
        ..legalTexts = {'terms': publicText(terms), 'privacy': publicText(privacy)};
      final container = ProviderContainer(overrides: [
        apiProvider.overrideWithValue(api),
        sessionStoreProvider.overrideWithValue(FakeSessionStore(refreshToken: 'stored-refresh-token')
          ..refreshExpiry = DateTime.now().toUtc().add(const Duration(days: 14))),
        sharedPreferencesProvider.overrideWithValue(await SharedPreferences.getInstance()),
        installedAppVersionProvider.overrideWithValue('1.4.0+7'),
        pushMessagingProvider.overrideWithValue(FakePushMessaging()),
      ]);
      addTearDown(container.dispose);

      await tester.pumpWidget(UncontrolledProviderScope(container: container, child: const KhadraApp()));
      await tester.pumpAndSettle();
      return (container, api);
    }

    Future<void> deepLink(WidgetTester tester, String location) async {
      await tester.binding.defaultBinaryMessenger.handlePlatformMessage(
        'flutter/navigation',
        const JSONMethodCodec().encodeMethodCall(MethodCall(
          'pushRouteInformation',
          <String, dynamic>{'location': location, 'state': null},
        )),
        (_) {},
      );
      await tester.pumpAndSettle();
    }

    testWidgets('with nothing pending, the app is the app', (tester) async {
      final (container, _) = await launch(tester);

      expect(container.read(sessionProvider).isSignedIn, isTrue);
      expect(find.byType(ConsentPromptScreen), findsNothing);
    });

    testWidgets('a text still to accept puts the prompt in front of everything, and holds every poll',
        (tester) async {
      final (container, _) = await launch(tester, pending: [terms]);

      expect(find.byType(ConsentPromptScreen), findsOneWidget);
      await deepLink(tester, '/profile');
      expect(find.byType(ConsentPromptScreen), findsOneWidget);
      expect(find.byType(ProfileScreen), findsNothing);

      var reads = 0;
      container.read(liveRefreshProvider).register(LiveSurface(id: 'probe', refresh: () async => ++reads > 0));
      container.read(liveRefreshProvider).touch('probe');
      expect(reads, 0, reason: 'behind the prompt every read would only be refused');
    });

    testWidgets('accepting takes the prompt away, and the app is where the router left it', (tester) async {
      final (container, api) = await launch(tester, pending: [terms]);
      // The app's own navigation while the prompt is up (a push tap does this): the router keeps it.
      container.read(routerProvider).go('/profile');
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const ValueKey('consent-agree')));
      await tester.pump();
      await tester.tap(find.byKey(const ValueKey('consent-accept')));
      await tester.pumpAndSettle();

      expect(api.acceptances.single.$1, ['v-terms-3']);
      expect(find.byType(ConsentPromptScreen), findsNothing);
      expect(find.byType(ProfileScreen), findsOneWidget);
      expect(container.read(consentRequiredProvider), isFalse);
    });

    testWidgets('a refusal mid-session puts it up at once', (tester) async {
      final (container, api) = await launch(tester);
      expect(find.byType(ConsentPromptScreen), findsNothing);

      // What ConsentInterceptor does on a 403 legal.consent_pending: a text came into force
      // while the app was open.
      api.pendingConsents = [privacy];
      container.read(consentRequiredProvider.notifier).state = true;
      await tester.pumpAndSettle();

      expect(find.byType(ConsentPromptScreen), findsOneWidget);
      expect(find.text(en.legalPrivacy), findsOneWidget);
    });

    testWidgets('signing out from it ends the session and lands on Get Started', (tester) async {
      final (container, api) = await launch(tester, pending: [terms]);

      await tester.tap(find.byKey(const ValueKey('consent-sign-out')));
      await tester.pumpAndSettle();

      expect(api.signOutCalls, 1);
      expect(container.read(sessionProvider).isSignedIn, isFalse);
      expect(find.byType(ConsentPromptScreen), findsNothing);
      expect(find.byType(WelcomeScreen), findsOneWidget);
    });

    // Advisor's review of Wave 7: the acceptance used to wait on /auth/me for the empty list, and a re-read
    // that failed left the old one in place — the prompt went straight back up over texts already accepted.
    testWidgets('an acceptance stands even when the account cannot be read again just then', (tester) async {
      final (container, api) = await launch(tester, pending: [terms]);
      api.meFails = true;

      await tester.tap(find.byKey(const ValueKey('consent-agree')));
      await tester.pump();
      await tester.tap(find.byKey(const ValueKey('consent-accept')));
      await tester.pumpAndSettle();
      await tester.pump(const Duration(seconds: 1));
      await tester.pumpAndSettle();

      expect(api.acceptances, hasLength(1));
      expect(find.byType(ConsentPromptScreen), findsNothing);
      expect(container.read(consentRequiredProvider), isFalse);
      expect(container.read(sessionProvider).user!.pendingConsents, isEmpty);
    });

    testWidgets('a text published since the acceptance is asked for again', (tester) async {
      final (container, api) = await launch(tester, pending: [terms]);
      api.pendingAfterAcceptance = [privacy];

      await tester.tap(find.byKey(const ValueKey('consent-agree')));
      await tester.pump();
      await tester.tap(find.byKey(const ValueKey('consent-accept')));
      await tester.pumpAndSettle();

      // The prompt itself sees the newer text in the acceptance's answer and asks for it.
      expect(find.byType(ConsentPromptScreen), findsOneWidget);
      expect(find.text(en.legalPrivacy), findsOneWidget);
      expect(container.read(consentRequiredProvider), isTrue);
    });

    // Advisor's review of Wave 7: the router's back-button dispatcher goes with the router, so Back on a text
    // opened from the prompt found nobody to answer it and closed the app.
    testWidgets('Android Back on a text opened from it returns to the prompt', (tester) async {
      final (_, api) = await launch(tester, pending: [terms]);
      // No public text: the reader opens and stops short of the WebView a test has not got.
      api.legalTexts = const {};
      final exits = <String>[];
      tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(SystemChannels.platform, (call) async {
        exits.add(call.method);
        return null;
      });
      addTearDown(() => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(SystemChannels.platform, null));

      await tester.tap(find.byKey(const ValueKey('consent-read-Terms')));
      await tester.pumpAndSettle();
      expect(find.byType(LegalTextScreen), findsOneWidget);

      await tester.binding.handlePopRoute();
      await tester.pumpAndSettle();

      expect(find.byType(LegalTextScreen), findsNothing);
      expect(find.byType(ConsentPromptScreen), findsOneWidget);
      expect(exits, isNot(contains('SystemNavigator.pop')));

      // On the prompt itself, Back leaves the app as before: there is nothing behind it.
      await tester.binding.handlePopRoute();
      await tester.pumpAndSettle();
      expect(exits, contains('SystemNavigator.pop'));
    });

    testWidgets('an update required outranks a text still to accept', (tester) async {
      final (container, _) = await launch(tester, pending: [terms]);
      expect(find.byType(ConsentPromptScreen), findsOneWidget);

      container.read(serverUpdateRefusalProvider.notifier).state = UpdateRequirement.fromRefusal(const {
        'code': 'app.update_required',
        'minimumSupportedVersion': '1.5.0',
        'updateUrl': 'https://example.org/khadra.apk',
      });
      await tester.pumpAndSettle();

      expect(find.byType(UpdateRequiredScreen), findsOneWidget);
      expect(find.byType(ConsentPromptScreen), findsNothing);
    });
  });
}

/// The API a stored session comes back to on a cold start: the rotation answers.
class _RestoringApi extends FakeApi {
  @override
  Future<AuthTokens> refresh(String refreshToken) async => FakeApi.fakeTokens();

  /// When set, `/auth/me` fails the way a dropped network does.
  bool meFails = false;

  @override
  Future<AuthUser> me() async {
    if (meFails) throw const ApiFailure(kind: ApiFailureKind.offline);
    return super.me();
  }
}
