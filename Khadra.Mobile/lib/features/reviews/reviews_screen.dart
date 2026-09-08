import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/api/api_failure_messages.dart';
import '../../core/format/formats.dart';
import '../../core/providers.dart';
import '../../core/router.dart';
import '../../core/theme/khadra_theme.dart';
import '../../core/widgets/khadra_widgets.dart';
import '../../l10n/app_localizations.dart';
import '../catalogue/search_providers.dart';

/// A rental office's reviews.
///
/// **No reviewer is named.** Who left a review is not something the platform asked
/// a customer's permission to publish, and a name beside the dates of a rental
/// says more than either fact on its own. The API does not send one.
///
/// A moderated review keeps its STAR and loses its text: hiding removes abusive
/// wording, never the score, or reporting a comment would be a way for an office
/// to erase the rating attached to it.
class ReviewsScreen extends ConsumerWidget {
  const ReviewsScreen({super.key, required this.dealerId});

  final String dealerId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final reviews = ref.watch(galleryReviewsProvider(dealerId));
    final gallery = ref.watch(galleryProvider(dealerId)).valueOrNull;
    final formats = ref.watch(formatsProvider);

    return Scaffold(
      appBar: AppBar(
        leading: const KhadraBack(fallback: Routes.search),
        title: Text(l10n.reviewsTitle),
      ),
      body: RefreshIndicator(
        onRefresh: () => ref.refresh(galleryReviewsProvider(dealerId).future),
        child: switch (reviews) {
          AsyncLoading() => const KhadraLoading(),
          AsyncError(:final error) => KhadraError(
              message: ApiFailure.from(error).messageFor(l10n),
              onRetry: () => ref.invalidate(galleryReviewsProvider(dealerId)),
            ),
          AsyncData(:final value) when value.items.isEmpty => ListView(
              children: [
                SizedBox(
                  height: MediaQuery.of(context).size.height * 0.5,
                  child: KhadraEmpty(
                    icon: Icons.star_outline_rounded,
                    title: l10n.reviewsEmpty,
                  ),
                ),
              ],
            ),
          AsyncData(:final value) when formats != null => ListView(
              padding: const EdgeInsets.fromLTRB(
                  Space.lg, Space.lg, Space.lg, Space.bottomInset),
              children: [
                if (gallery != null && gallery.averageRating != null)
                  KhadraCard(
                    child: Row(
                      children: [
                        LatinRun(
                          gallery.averageRating!.toStringAsFixed(1),
                          style: const TextStyle(
                              fontSize: 32, fontWeight: FontWeight.w700),
                        ),
                        const SizedBox(width: Space.lg),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              KhadraStars(
                                  rating: gallery.averageRating, size: 20),
                              const SizedBox(height: Space.xs),
                              Text(
                                l10n.galleryReviewCount(gallery.reviewCount),
                                style: const TextStyle(
                                    color: KhadraColors.neutral600,
                                    fontSize: 13),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ),
                const SizedBox(height: Space.lg),
                for (final review in value.items) ...[
                  _ReviewCard(review: review, formats: formats),
                  const SizedBox(height: Space.md),
                ],
                const SizedBox(height: Space.md),
                Text(
                  l10n.reviewsRatingIsOfficeNote,
                  textAlign: TextAlign.center,
                  style: const TextStyle(
                      color: KhadraColors.neutral500, fontSize: 12),
                ),
              ],
            ),
          _ => const KhadraLoading(),
        },
      ),
    );
  }
}

class _ReviewCard extends StatelessWidget {
  const _ReviewCard({required this.review, required this.formats});

  final GalleryReview review;
  final Formats formats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return KhadraCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              KhadraStars(rating: review.rating, size: 16),
              const Spacer(),
              Text(
                formats.longDate(review.createdAt),
                style: const TextStyle(
                    color: KhadraColors.neutral500, fontSize: 12),
              ),
            ],
          ),
          if (review.isHidden) ...[
            const SizedBox(height: Space.sm),
            Text(
              // Said out loud rather than shown as a blank: the star beside it
              // still counts, and a silent gap would look like a rating with no
              // reason behind it.
              l10n.reviewHidden,
              style: const TextStyle(
                fontSize: 13,
                height: 1.45,
                fontStyle: FontStyle.italic,
                color: KhadraColors.neutral500,
              ),
            ),
          ] else if (review.comment != null && review.comment!.isNotEmpty) ...[
            const SizedBox(height: Space.sm),
            Text(
              review.comment!,
              style: const TextStyle(fontSize: 14, height: 1.5),
            ),
          ],
        ],
      ),
    );
  }
}
