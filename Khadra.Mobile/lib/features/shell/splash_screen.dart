import 'package:flutter/material.dart';

import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';

/// Held only while the cold-start token rotation is in flight.
///
/// It is not a brand animation with a timer. The router leaves it the instant the
/// session resolves, so on a warm start with a fast network it is barely seen.
class SplashScreen extends StatelessWidget {
  const SplashScreen({super.key});

  @override
  Widget build(BuildContext context) => const Scaffold(
        backgroundColor: KhadraColors.surface,
        body: Center(
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              KhadraWordmark(),
              SizedBox(height: Space.xxl),
              SizedBox(
                width: 22,
                height: 22,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
            ],
          ),
        ),
      );
}
