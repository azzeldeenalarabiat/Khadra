import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/push/push_trace.dart';
import '../../core/theme/khadra_theme.dart';

/// TEMPORARY — STAGING BUILDS ONLY: what [PushTrace] recorded, newest first, to read on
/// the phone or copy into a report. Never in a production build: Profile offers it only
/// when [PushTrace.enabled]. Developer text, in one language on purpose; nothing here is
/// shown to a customer.
class PushTraceScreen extends StatefulWidget {
  const PushTraceScreen({super.key});

  @override
  State<PushTraceScreen> createState() => _PushTraceScreenState();
}

class _PushTraceScreenState extends State<PushTraceScreen> {
  List<String> _background = const [];

  @override
  void initState() {
    super.initState();
    PushTrace.changes.addListener(_reload);
    _reload();
  }

  @override
  void dispose() {
    PushTrace.changes.removeListener(_reload);
    super.dispose();
  }

  Future<void> _reload() async {
    final background = await PushTrace.backgroundLines();
    if (mounted) setState(() => _background = background);
  }

  String get _report => [
        '# app',
        ...PushTrace.lines.reversed,
        '# background isolate',
        ..._background.reversed,
      ].join('\n');

  @override
  Widget build(BuildContext context) => Directionality(
        // rtl-audit: allow — developer diagnostics, English only, never shown to a customer (W4-9).
        textDirection: TextDirection.ltr,
        child: Scaffold(
          appBar: AppBar(
            // rtl-audit: allow — developer diagnostics, English only (W4-9).
            title: const Text('push trace (staging)'),
            actions: [
              IconButton(
                icon: const Icon(Icons.copy_all_outlined),
                onPressed: () => Clipboard.setData(ClipboardData(text: _report)),
              ),
              IconButton(
                icon: const Icon(Icons.delete_outline),
                onPressed: () async {
                  await PushTrace.clear();
                  await _reload();
                },
              ),
            ],
          ),
          body: ListView(
            padding: const EdgeInsets.all(Space.lg),
            children: [
              SelectableText(
                _report,
                style: const TextStyle(fontFamily: 'monospace', fontSize: 12, height: 1.4),
              ),
            ],
          ),
        ),
      );
}
