import 'dart:async';

import 'package:khadra_mobile/core/push/push_messaging.dart';

/// A push platform that answers from memory, so the rules are tested without Firebase.
class FakePushMessaging implements PushMessaging {
  FakePushMessaging({this.available = true, this.permitted = true, this.currentToken = 'token-1'});

  bool available;
  bool permitted;
  String? currentToken;
  int permissionRequests = 0;
  int deletions = 0;
  PushEvent? launch;
  final List<PushEvent> shown = [];
  final foregroundController = StreamController<PushEvent>.broadcast();
  final tapController = StreamController<PushEvent>.broadcast();
  final refreshController = StreamController<String>.broadcast();

  @override
  Future<bool> initialize() async => available;

  @override
  Future<bool> requestPermission() async {
    permissionRequests++;
    return permitted;
  }

  @override
  Future<String?> token() async => currentToken;

  @override
  Stream<String> get tokenRefreshes => refreshController.stream;

  @override
  Future<void> deleteToken() async {
    deletions++;
    currentToken = null;
  }

  @override
  Stream<PushEvent> get foreground => foregroundController.stream;

  @override
  Stream<PushEvent> get taps => tapController.stream;

  @override
  Future<PushEvent?> launchTap() async => launch;

  @override
  Future<void> show(PushEvent event) async => shown.add(event);
}
