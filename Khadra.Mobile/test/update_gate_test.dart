
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/api/api_failure_messages.dart';
import 'package:khadra_mobile/core/config/update_requirement.dart';
import 'package:khadra_mobile/core/providers.dart';
import 'package:khadra_mobile/core/router.dart';
import 'package:khadra_mobile/features/profile/profile_screen.dart';
import 'package:khadra_mobile/features/update/update_required_screen.dart';
import 'package:khadra_mobile/l10n/app_localizations.dart';
import 'package:khadra_mobile/main.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fake_api.dart';

/// What the API answers an old build with — its shape pinned server-side by
/// `MobileAppVersionGateTests`.
const _refusal = <String, dynamic>{
  'type': 'https://httpstatuses.com/426',
  'title': 'Update the Khadra app to continue',
  'status': 426,
  'code': 'app.update_required',
  'minimumSupportedVersion': '1.1.0',
  'updateUrl': 'https://example.org/khadra.apk',
};

/// The mandatory-update gate, on the phone's side.
///
/// The API refuses a build older than its minimum with `426 app.update_required`
/// on every call, and publishes the minimum in `/app-config`. This build must
/// turn either into ONE full-screen answer that nothing gets past — no route, no
/// deep link — in both languages, without ever treating the refusal as a verdict
/// on the customer's sign-in: the build is too old, not the credentials.
///
/// The wire half — the header, and a signed-in session meeting a 426 — is in
/// `update_gate_http_test.dart`, which needs real sockets this file cannot have.
void main() {
  group('the words', () {
    ApiFailure refusal() => ApiFailure.from(DioException(
          requestOptions: RequestOptions(path: '/api/v1/auth/login'),
          type: DioExceptionType.badResponse,
          response: Response<dynamic>(
            requestOptions: RequestOptions(path: '/api/v1/auth/login'),
            statusCode: 426,
            data: _refusal,
          ),
        ));

    test('a refused sign-in reads as an update, never as a wrong password', () async {
      final en = await AppLocalizations.delegate.load(const Locale('en'));
      final ar = await AppLocalizations.delegate.load(const Locale('ar'));

      expect(refusal().kind, ApiFailureKind.updateRequired);
      expect(refusal().messageFor(en), 'Update the Khadra app to continue');
      expect(refusal().messageFor(ar), 'حدّث تطبيق خضرا للمتابعة');
      expect(refusal().messageFor(en), isNot(en.errorAuthInvalidCredentials));
    });
  });

  group('the published minimum', () {
    test('is absent on an API that predates it, and then nothing is refused', () {
      // The window where a 1.1.0 build meets the old API: it must work there, not
      // lock itself out.
      expect(FakeApi.fakeConfig().mobileApp.minimumSupportedVersion, isNull);
      expect(FakeApi.fakeConfig().mobileApp.updateUrl, isNull);
    });

    test('is read as a version, and a link only when a phone can open it', () {
      final config = FakeApi.fakeConfig(mobileApp: {
        'minimumSupportedVersion': '1.1.0',
        'updateUrl': 'https://example.org/khadra.apk',
      }).mobileApp;

      expect(config.minimumSupportedVersion.toString(), '1.1.0');
      expect(config.updateUrl, Uri.parse('https://example.org/khadra.apk'));

      final garbled = FakeApi.fakeConfig(mobileApp: {
        'minimumSupportedVersion': 'latest',
        'updateUrl': 'javascript:alert(1)',
      }).mobileApp;
      // Unreadable: the app defers to the server rather than guessing.
      expect(garbled.minimumSupportedVersion, isNull);
      expect(garbled.updateUrl, isNull);
    });
  });

  group('the whole app, at the root', () {
    const minimum = {
      'minimumSupportedVersion': '1.1.0',
      'updateUrl': 'https://example.org/khadra.apk',
    };

    Future<ProviderContainer> launch(
      WidgetTester tester, {
      required String? installed,
      Map<String, dynamic>? mobileApp = minimum,
      String? language,
    }) async {
      tester.view.physicalSize = const Size(1000, 2000);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      SharedPreferences.setMockInitialValues(
          {if (language != null) 'khadra.locale': language});
      final preferences = await SharedPreferences.getInstance();

      final container = ProviderContainer(overrides: [
        apiProvider.overrideWithValue(FakeApi()..mobileApp = mobileApp),
        sessionStoreProvider.overrideWithValue(FakeSessionStore()),
        sharedPreferencesProvider.overrideWithValue(preferences),
        installedAppVersionProvider.overrideWithValue(installed),
      ]);
      addTearDown(container.dispose);

      await tester.pumpWidget(
        UncontrolledProviderScope(container: container, child: const KhadraApp()),
      );
      await tester.pumpAndSettle();
      return container;
    }

    /// A link arriving from the OS, the way a tapped notification or a shared URL
    /// reaches the app.
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

    testWidgets('below the minimum, the update screen IS the app — in English',
        (tester) async {
      await launch(tester, installed: '1.0.0+1', language: 'en');

      expect(find.byType(UpdateRequiredScreen), findsOneWidget);
      expect(find.text('Update the Khadra app to continue'), findsOneWidget);
      expect(
        find.text(
            'This version of the app is no longer supported. Install the latest version to keep browsing cars and managing your bookings. Updating won\'t sign you out.'),
        findsOneWidget,
      );
      expect(find.text('Installed: 1.0.0 · Required: 1.1.0 or later'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Update now'), findsOneWidget);
      // Nothing of the app behind it.
      expect(find.byType(ProfileScreen), findsNothing);
    });

    testWidgets('and in Arabic, right to left', (tester) async {
      await launch(tester, installed: '1.0.0+1', language: 'ar');

      expect(find.text('حدّث تطبيق خضرا للمتابعة'), findsOneWidget);
      expect(
        find.text(
            'هذا الإصدار من التطبيق لم يعد مدعومًا. ثبّت أحدث إصدار لتواصل تصفّح السيارات وإدارة حجوزاتك. التحديث لن يسجّل خروجك.'),
        findsOneWidget,
      );
      expect(find.text('المثبّت: 1.0.0 · المطلوب: 1.1.0 أو أحدث'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'حدّث الآن'), findsOneWidget);
      expect(
        Directionality.of(tester.element(find.byType(UpdateRequiredScreen))),
        TextDirection.rtl,
      );
    });

    testWidgets('with no link published, it says where to look instead of inventing one',
        (tester) async {
      await launch(tester,
          installed: '1.0.0+1', mobileApp: {'minimumSupportedVersion': '1.1.0'});

      expect(find.byType(FilledButton), findsNothing);
      expect(find.text('Get the latest version from wherever you downloaded Khadra.'),
          findsOneWidget);
    });

    testWidgets('at the minimum, the app starts normally', (tester) async {
      final container = await launch(tester, installed: '1.1.0+2');

      expect(find.byType(UpdateRequiredScreen), findsNothing);
      // And a link reaches its screen — the control for the deep-link test below:
      // without it, "the link did nothing while blocked" would prove nothing.
      await deepLink(tester, '/profile');
      expect(find.byType(ProfileScreen), findsOneWidget);
      expect(container.read(updateRequirementProvider), isNull);
    });

    testWidgets('above it too, compared as versions and not as text', (tester) async {
      await launch(tester,
          installed: '1.10.0+40', mobileApp: {'minimumSupportedVersion': '1.9.0'});

      expect(find.byType(UpdateRequiredScreen), findsNothing);
    });

    testWidgets('when the installed version is unknown, it defers to the server',
        (tester) async {
      await launch(tester, installed: null);

      expect(find.byType(UpdateRequiredScreen), findsNothing);
    });

    testWidgets('a deep link while blocked leaves the update screen in control',
        (tester) async {
      final container = await launch(tester, installed: '1.0.0+1');

      await deepLink(tester, '/profile');
      expect(find.byType(UpdateRequiredScreen), findsOneWidget);
      expect(find.byType(ProfileScreen), findsNothing);

      // Nor can the app's own navigation get past it.
      container.read(routerProvider).go('/profile');
      await tester.pumpAndSettle();
      expect(find.byType(UpdateRequiredScreen), findsOneWidget);
      expect(find.byType(ProfileScreen), findsNothing);
    });

    testWidgets('a refusal mid-session puts it up at once, and it stays up',
        (tester) async {
      // A minimum raised while the app was open: config said nothing when it
      // launched, then a call comes back 426.
      final container = await launch(tester, installed: '1.1.0+2', mobileApp: null);
      expect(find.byType(UpdateRequiredScreen), findsNothing);

      container.read(serverUpdateRefusalProvider.notifier).state =
          UpdateRequirement.fromRefusal(_refusal);
      await tester.pumpAndSettle();
      expect(find.byType(UpdateRequiredScreen), findsOneWidget);

      // Sticky: nothing this process does makes the build newer.
      await deepLink(tester, '/profile');
      expect(find.byType(UpdateRequiredScreen), findsOneWidget);
    });
  });
}
