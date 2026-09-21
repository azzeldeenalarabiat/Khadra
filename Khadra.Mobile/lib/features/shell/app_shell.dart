import 'dart:async';

import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../notifications/notification_providers.dart';

/// The five tabs, the badge on the alerts one, and the guard on the way out.
class AppShell extends ConsumerStatefulWidget {
  const AppShell({super.key, required this.shell});

  final StatefulNavigationShell shell;

  @override
  ConsumerState<AppShell> createState() => _AppShellState();
}

class _AppShellState extends ConsumerState<AppShell> {
  /// How long the first Back press counts for.
  ///
  /// The message announcing it is shown for the SAME duration, so what is on
  /// screen and what is being counted agree. A hint still sitting there after
  /// the window has closed is a trap: the second press would exit nothing and
  /// the reader would have no idea why.
  static const _exitWindow = Duration(seconds: 2);

  DateTime? _backPressedAt;

  /// Android Back on a tab, which is the root of the app.
  ///
  /// None of the five tabs has anything beneath it: `indexedStack` switches
  /// branches without pushing history, and every screen that IS pushed — a car,
  /// a booking, a dispute, the account screens — lives on the root navigator
  /// ABOVE this one. So a pushed route pops normally and never reaches here, and
  /// nested Back needs no code at all. When this runs, the only thing left to pop
  /// is Khadra itself.
  ///
  /// Which is what it did: one press, gone, mid-search, with the city and the
  /// dates the customer had set. Two steps now stand in the way, and they are the
  /// two Android asks for.
  ///
  /// **From any other tab, Back returns to Home** rather than leaving. It is the
  /// platform's own guidance for a bottom bar, and without it the guard would be
  /// a lie: Back on Bookings would still have closed the app with no warning
  /// while Back on Home asked twice.
  ///
  /// **From Home, the first press offers the exit and the second takes it.**
  ///
  /// The cost is Android's predictive-back animation on the tabs, because a route
  /// that may refuse to pop cannot be animated away before it decides. Every
  /// pushed screen keeps it: none of them declines a pop. The manifest does not
  /// opt into the new back API today, so Android uses the legacy path here in any
  /// case — enabling it is a separate decision, not part of this change.
  void _onPopInvoked(bool didPop, Object? result) {
    if (didPop) return;

    final shell = widget.shell;
    if (shell.currentIndex != _homeTab) {
      // Forget any earlier press. Back on Home, a tap to Bookings and two more
      // Backs inside one window would otherwise leave the app on the second
      // press without ever having offered the exit on Home.
      _backPressedAt = null;
      shell.goBranch(_homeTab);
      return;
    }

    final now = DateTime.now();
    final previous = _backPressedAt;
    if (previous != null && now.difference(previous) <= _exitWindow) {
      ScaffoldMessenger.of(context).hideCurrentSnackBar();
      // Not `Navigator.pop`: there is nothing under this route. This is the
      // platform's own "leave the app", which on Android backgrounds it the way
      // the Home button does rather than destroying anything.
      unawaited(SystemNavigator.pop());
      return;
    }

    _backPressedAt = now;
    showKhadraMessage(
      context,
      AppLocalizations.of(context).backAgainToExit,
      duration: _exitWindow,
    );
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final shell = widget.shell;
    final signedIn = ref.watch(sessionProvider).isSignedIn;

    // Only asked for when there is somebody to ask about. A signed-out browser
    // must not generate a 401 a second after opening the app.
    final unread = signedIn
        ? (ref.watch(unreadNotificationCountProvider).valueOrNull ?? 0)
        : 0;

    return PopScope(
      // The web build takes no part in this. `SystemNavigator.pop()` means
      // nothing in a browser, and Back there is the browser's own button driving
      // the router — intercepting it would break history, not protect anything.
      canPop: kIsWeb,
      onPopInvokedWithResult: _onPopInvoked,
      child: Scaffold(
        body: shell,
        // The design separates the bar from the page with a HAIRLINE, not a shadow.
        // Material's own divider is drawn by elevation, which this theme sets to zero.
        bottomNavigationBar: DecoratedBox(
          decoration: const BoxDecoration(
            border: Border(top: BorderSide(color: KhadraColors.divider)),
          ),
          child: NavigationBar(
            selectedIndex: shell.currentIndex,
            onDestinationSelected: (index) {
              // Tapping the tab you are already on returns it to its root, which is
              // what every phone user expects and what gets somebody out of a filter
              // sheet they have got lost in.
              shell.goBranch(index, initialLocation: index == shell.currentIndex);

              // The badge is a poll, so a deliberate visit is the moment to make it
              // honest rather than up to a minute stale.
              if (index == _alertsTab && signedIn) {
                unawaited(ref.refresh(unreadNotificationCountProvider.future));
              }
            },
            destinations: [
              NavigationDestination(
                icon: const Icon(Icons.search_outlined),
                selectedIcon: const Icon(Icons.search),
                label: l10n.navHome,
              ),
              NavigationDestination(
                icon: const Icon(Icons.event_note_outlined),
                selectedIcon: const Icon(Icons.event_note),
                label: l10n.navBookings,
              ),
              NavigationDestination(
                icon: Badge.count(
                  count: unread,
                  isLabelVisible: unread > 0,
                  child: const Icon(Icons.notifications_none),
                ),
                selectedIcon: Badge.count(
                  count: unread,
                  isLabelVisible: unread > 0,
                  child: const Icon(Icons.notifications),
                ),
                label: l10n.navNotifications,
              ),
              // The heart the catalogue and the car page already use for saving.
              // It opens the SAME screen as My Account → Saved cars, reading the
              // same shortlist from the server; the entry in My Account stays
              // exactly where it was.
              NavigationDestination(
                icon: const Icon(Icons.favorite_border),
                selectedIcon: const Icon(Icons.favorite),
                label: l10n.navSaved,
              ),
              NavigationDestination(
                icon: const Icon(Icons.person_outline),
                selectedIcon: const Icon(Icons.person),
                label: l10n.navProfile,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// The order of the bar, named rather than left as numbers.
///
/// These indices are the branch order in `router.dart` AND the destination order
/// below, and two places read one of them: Back returns to Home, and a visit to
/// Alerts refreshes its badge. Inserting Saved between Alerts and Profile is
/// exactly the edit that silently moves a bare `2`.
const int _homeTab = 0;
const int _alertsTab = 2;
