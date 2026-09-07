import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import '../../l10n/app_localizations.dart';
import '../notifications/notification_providers.dart';

/// The four tabs, and the badge on the alerts one.
class AppShell extends ConsumerWidget {
  const AppShell({super.key, required this.shell});

  final StatefulNavigationShell shell;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final signedIn = ref.watch(sessionProvider).isSignedIn;

    // Only asked for when there is somebody to ask about. A signed-out browser
    // must not generate a 401 a second after opening the app.
    final unread = signedIn
        ? (ref.watch(unreadNotificationCountProvider).valueOrNull ?? 0)
        : 0;

    return Scaffold(
      body: shell,
      bottomNavigationBar: NavigationBar(
        selectedIndex: shell.currentIndex,
        onDestinationSelected: (index) {
          // Tapping the tab you are already on returns it to its root, which is
          // what every phone user expects and what gets somebody out of a filter
          // sheet they have got lost in.
          shell.goBranch(index, initialLocation: index == shell.currentIndex);

          // The badge is a poll, so a deliberate visit is the moment to make it
          // honest rather than up to a minute stale.
          if (index == 2 && signedIn) {
            unawaited(ref.refresh(unreadNotificationCountProvider.future));
          }
        },
        destinations: [
          NavigationDestination(
            icon: const Icon(Icons.search_outlined),
            selectedIcon: const Icon(Icons.search),
            label: l10n.navBrowse,
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
          NavigationDestination(
            icon: const Icon(Icons.person_outline),
            selectedIcon: const Icon(Icons.person),
            label: l10n.navProfile,
          ),
        ],
      ),
    );
  }
}
