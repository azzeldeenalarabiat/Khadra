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
import '../../core/live/live_refresh.dart';
import '../../core/live/live_surfaces.dart';
import '../notifications/notification_providers.dart';

/// The five tabs, the badge on the alerts one, and the guard on the way out.
class AppShell extends ConsumerStatefulWidget {
  const AppShell({super.key, required this.shell});

  final StatefulNavigationShell shell;

  @override
  ConsumerState<AppShell> createState() => _AppShellState();
}

class _AppShellState extends ConsumerState<AppShell> with WidgetsBindingObserver {
  /// Held rather than read on demand, and assigned EAGERLY in `initState`.
  ///
  /// `ref` throws once the widget is disposed, and disposing is exactly when the heartbeat has to
  /// be stopped — so a `late final` initialiser would be evaluated for the first time in `dispose`,
  /// which is the one place it cannot run.
  late final LiveRefresh _live;

  @override
  void initState() {
    super.initState();
    _live = ref.read(liveRefreshProvider);
    WidgetsBinding.instance.addObserver(this);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      ref.read(visibleTabProvider.notifier).state = widget.shell.currentIndex;
      // No `becameVisible()` here. The screens on the first tab load themselves the moment they are
      // watched; firing trigger A on top of that asked for everything twice on every cold start.
      registerCustomerSurfaces(ref);
    });
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    // Whoever registers, unregisters. The last one out stops the heartbeat, so a shell that is gone
    // leaves no timer behind waking up to poll for a screen nobody is looking at.
    for (final id in Surfaces.all) {
      _live.unregister(id);
    }
    super.dispose();
  }

  /// The app going to and from the front, which is the only thing that may start or stop a poll.
  ///
  /// Anything but `resumed` stops every one of them. Not a battery nicety: each request runs
  /// `AuthInterceptor.onRequest`, which rotates the refresh token whenever the access token is
  /// stale — so an app left in the background would have rotated every few minutes for as long as
  /// it sat there, and each rotation is a chance to hit pre-launch items 126 and 128 and sign
  /// somebody out of a session they never left.
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    final resumed = state == AppLifecycleState.resumed;
    ref.read(appResumedProvider.notifier).state = resumed;
    ref.read(liveRefreshProvider).setResumed(resumed);
  }

  /// A tab became the one on screen.
  ///
  /// Hooked HERE rather than in `onDestinationSelected`, because a tap is not the only way a branch
  /// changes: Android Back calls `goBranch(_homeTab)` a few lines below, a notification row and a
  /// deep link both use `context.go`, and none of those passes through the bar's callback. The
  /// index is the fact; the tap is one of several causes of it.
  @override
  void didUpdateWidget(AppShell oldWidget) {
    super.didUpdateWidget(oldWidget);
    final index = widget.shell.currentIndex;
    if (index == oldWidget.shell.currentIndex) return;

    // After the frame, not during it. Riverpod refuses a write from inside a widget life-cycle —
    // two widgets listening to one provider could otherwise come out of the same build holding
    // different states. A frame later is the same instant to the person holding the phone.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      ref.read(visibleTabProvider.notifier).state = index;
      // Trigger A, through the twenty-second floor — so flicking between tabs is not a burst of
      // requests, and a tab left for a minute is current again by the time it is read.
      ref.read(liveRefreshProvider).becameVisible();
    });
  }

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

              // The refresh that used to live here — "a deliberate visit is the moment to make the
              // badge honest" — is now `didUpdateWidget` above, which sees every way a branch
              // becomes current rather than only a tap. Tapping the tab you are already on is the
              // one case that does NOT change the index, and it is a scroll-to-top, not a re-read.
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
/// These indices are the branch order in `router.dart`, the destination order below, AND which tab
/// each live surface polls on — so they are written down ONCE, in `Tabs`, rather than here as well.
/// Inserting Saved between Alerts and Profile is exactly the edit that silently moves a bare `2`,
/// and it would have had to move it in two files.
const int _homeTab = Tabs.home;
