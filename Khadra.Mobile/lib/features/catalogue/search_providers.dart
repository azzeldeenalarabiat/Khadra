import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/dtos.dart';
import '../../core/api/api_failure.dart';
import '../../core/providers.dart';

/// How a customer has narrowed the search.
///
/// The dates are BOTH or NEITHER. The API refuses half a period rather than
/// ignoring it, and "is this car free" has no answer without a period to ask
/// about — so the filter carries them as a pair and clears them as a pair.
@immutable
class SearchFilter {
  const SearchFilter({
    this.text,
    this.cityId,
    this.carTypeId,
    this.transmission,
    this.minSeats,
    this.minDailyRate,
    this.maxDailyRate,
    this.deliveryOnly = false,
    this.pickupAt,
    this.returnAt,
  });

  final String? text;
  final String? cityId;
  final String? carTypeId;
  final String? transmission;
  final int? minSeats;
  final num? minDailyRate;
  final num? maxDailyRate;
  final bool deliveryOnly;
  final DateTime? pickupAt;
  final DateTime? returnAt;

  bool get hasDates => pickupAt != null && returnAt != null;

  /// What the chip on the filter button counts. The text search is excluded: it
  /// has its own visible box, and counting it would say "1 filter" for something
  /// the customer can already see.
  int get activeCount => [
        cityId,
        carTypeId,
        transmission,
        minSeats,
        minDailyRate,
        maxDailyRate,
        deliveryOnly ? true : null,
        hasDates ? true : null,
      ].where((value) => value != null).length;

  SearchFilter copyWith({
    Object? text = _unset,
    Object? cityId = _unset,
    Object? carTypeId = _unset,
    Object? transmission = _unset,
    Object? minSeats = _unset,
    Object? minDailyRate = _unset,
    Object? maxDailyRate = _unset,
    bool? deliveryOnly,
    Object? pickupAt = _unset,
    Object? returnAt = _unset,
  }) =>
      SearchFilter(
        text: text == _unset ? this.text : text as String?,
        cityId: cityId == _unset ? this.cityId : cityId as String?,
        carTypeId: carTypeId == _unset ? this.carTypeId : carTypeId as String?,
        transmission:
            transmission == _unset ? this.transmission : transmission as String?,
        minSeats: minSeats == _unset ? this.minSeats : minSeats as int?,
        minDailyRate:
            minDailyRate == _unset ? this.minDailyRate : minDailyRate as num?,
        maxDailyRate:
            maxDailyRate == _unset ? this.maxDailyRate : maxDailyRate as num?,
        deliveryOnly: deliveryOnly ?? this.deliveryOnly,
        pickupAt: pickupAt == _unset ? this.pickupAt : pickupAt as DateTime?,
        returnAt: returnAt == _unset ? this.returnAt : returnAt as DateTime?,
      );

  /// Keeps the dates and the text, drops the rest. "Clear all" on the filter sheet
  /// should not silently un-choose the dates the customer is shopping for.
  SearchFilter cleared() => SearchFilter(
        text: text,
        pickupAt: pickupAt,
        returnAt: returnAt,
      );

  static const _unset = Object();

  @override
  bool operator ==(Object other) =>
      other is SearchFilter &&
      other.text == text &&
      other.cityId == cityId &&
      other.carTypeId == carTypeId &&
      other.transmission == transmission &&
      other.minSeats == minSeats &&
      other.minDailyRate == minDailyRate &&
      other.maxDailyRate == maxDailyRate &&
      other.deliveryOnly == deliveryOnly &&
      other.pickupAt == pickupAt &&
      other.returnAt == returnAt;

  @override
  int get hashCode => Object.hash(text, cityId, carTypeId, transmission, minSeats,
      minDailyRate, maxDailyRate, deliveryOnly, pickupAt, returnAt);
}

final searchFilterProvider =
    StateProvider<SearchFilter>((ref) => const SearchFilter());

/// One page of results, plus whatever pages were loaded before it.
class SearchResults {
  const SearchResults({
    required this.listings,
    required this.totalCount,
    required this.page,
    required this.hasMore,
    this.loadingMore = false,
  });

  final List<CatalogueListing> listings;
  final int totalCount;
  final int page;
  final bool hasMore;
  final bool loadingMore;

  static const empty =
      SearchResults(listings: [], totalCount: 0, page: 1, hasMore: false);
}

/// The catalogue, paged.
///
/// `AsyncNotifier` rather than a `FutureProvider` because the list ACCUMULATES:
/// page two is appended to page one, and only a change of filter starts over.
class SearchResultsNotifier extends AutoDisposeAsyncNotifier<SearchResults> {
  @override
  Future<SearchResults> build() async {
    // Rebuilds whenever the filter changes, which is what resets paging.
    final filter = ref.watch(searchFilterProvider);
    return _fetch(filter, page: 1, existing: const []);
  }

  Future<void> loadMore() async {
    final current = state.valueOrNull;
    if (current == null || !current.hasMore || current.loadingMore) return;

    state = AsyncData(SearchResults(
      listings: current.listings,
      totalCount: current.totalCount,
      page: current.page,
      hasMore: current.hasMore,
      loadingMore: true,
    ));

    final filter = ref.read(searchFilterProvider);
    try {
      state = AsyncData(
        await _fetch(filter, page: current.page + 1, existing: current.listings),
      );
    } on ApiFailure {
      // The pages already loaded are still good. Replacing them with an error
      // because page three failed would throw away what the customer is reading.
      state = AsyncData(SearchResults(
        listings: current.listings,
        totalCount: current.totalCount,
        page: current.page,
        hasMore: current.hasMore,
      ));
      rethrow;
    }
  }

  Future<SearchResults> _fetch(
    SearchFilter filter,
    { required int page,
    required List<CatalogueListing> existing }
  ) async {
    final result = await ref.read(apiProvider).searchVehicles(
          cityId: filter.cityId,
          carTypeId: filter.carTypeId,
          minDailyRate: filter.minDailyRate,
          maxDailyRate: filter.maxDailyRate,
          transmission: filter.transmission,
          minSeats: filter.minSeats,
          deliveryOnly: filter.deliveryOnly,
          text: filter.text,
          // Both or neither: the API refuses half a period.
          pickupAt: filter.hasDates ? filter.pickupAt : null,
          returnAt: filter.hasDates ? filter.returnAt : null,
          page: page,
          pageSize: 20,
        );

    return SearchResults(
      listings: [...existing, ...result.items],
      totalCount: result.totalCount,
      page: result.page,
      hasMore: result.hasNext,
    );
  }
}

final searchResultsProvider =
    AutoDisposeAsyncNotifierProvider<SearchResultsNotifier, SearchResults>(
  SearchResultsNotifier.new,
);

/// One car in full. Keyed by id AND by the chosen dates, because the answer to
/// "is it free" depends on them.
final vehicleProvider = FutureProvider.autoDispose
    .family<CatalogueVehicle, ({String id, DateTime? from, DateTime? to})>(
  (ref, key) => ref.watch(apiProvider).vehicle(
        key.id,
        pickupAt: key.from,
        returnAt: key.to,
      ),
);

final galleryProvider = FutureProvider.autoDispose.family<PublicGallery, String>(
  (ref, dealerId) => ref.watch(apiProvider).gallery(dealerId),
);

final galleryVehiclesProvider = FutureProvider.autoDispose
    .family<Paged<CatalogueListing>, String>(
  (ref, dealerId) =>
      ref.watch(apiProvider).searchVehicles(dealerId: dealerId, pageSize: 50),
);

final galleryReviewsProvider = FutureProvider.autoDispose
    .family<Paged<GalleryReview>, String>(
  (ref, dealerId) => ref.watch(apiProvider).galleryReviews(dealerId, pageSize: 50),
);

/// The server's price for a named rental.
///
/// Requested BEFORE the booking button is shown, so a customer never sees a price
/// beside a button that would be refused. Every figure on it is the server's, and
/// the day count in particular: the app never subtracts two dates to find it.
final quoteProvider = FutureProvider.autoDispose.family<
    RentalQuote,
    ({
      String vehicleId,
      DateTime pickupAt,
      DateTime returnAt,
      String pickupMethod,
      double? latitude,
      double? longitude,
    })>(
  (ref, key) => ref.watch(apiProvider).quote(
        vehicleId: key.vehicleId,
        pickupAt: key.pickupAt,
        returnAt: key.returnAt,
        pickupMethod: key.pickupMethod,
        latitude: key.latitude,
        longitude: key.longitude,
      ),
);
