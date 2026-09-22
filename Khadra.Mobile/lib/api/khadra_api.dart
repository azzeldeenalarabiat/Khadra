import 'dart:typed_data';

import 'package:dio/dio.dart';

import '../core/api/api_client.dart';
import '../core/api/api_failure.dart';
import '../core/api/auth_interceptor.dart';
import 'dtos.dart';

/// Every endpoint this app calls, in one place.
///
/// Thin by design: each method names a route, shapes the query and reads the
/// response into a DTO. No decisions live here, because the server makes all of
/// them — a price, a day count, whether a car is free, what a cancellation costs.
///
/// Endpoints marked anonymous carry `AuthInterceptor.anonymous`. That matters for
/// more than tidiness: a 401 from sign-in means "wrong password", and letting the
/// refresh machinery treat it as an expired session would clear a stored token
/// belonging to whoever was already signed in on the device.
class KhadraApi {
  KhadraApi(this._client);

  final ApiClient _client;

  Map<String, dynamic> _object(dynamic body) =>
      body is Map<String, dynamic> ? body : const <String, dynamic>{};

  // ── Platform ────────────────────────────────────────────────────────────────

  Future<AppConfig> appConfig() async => AppConfig.fromJson(
        _object(await _client.get<dynamic>(
          '/api/v1/app-config',
          options: AuthInterceptor.anonymous(),
        )),
      );

  Future<List<Lookup>> cities() async => (await _client.get<List<dynamic>>(
        '/api/v1/cities',
        options: AuthInterceptor.anonymous(),
      ))
          .whereType<Map<String, dynamic>>()
          .map(Lookup.fromJson)
          .toList();

  Future<List<Lookup>> carTypes() async => (await _client.get<List<dynamic>>(
        '/api/v1/car-types',
        options: AuthInterceptor.anonymous(),
      ))
          .whereType<Map<String, dynamic>>()
          .map(Lookup.fromJson)
          .toList();

  // ── Authentication ──────────────────────────────────────────────────────────

  Future<AuthTokens> signIn(String email, String password) async =>
      AuthTokens.fromJson(_object(await _client.post<dynamic>(
        '/api/v1/auth/login',
        body: {'email': email, 'password': password},
        options: AuthInterceptor.anonymous(),
      )));

  Future<RegisteredUser> register({
    required String email,
    required String password,
    required String fullName,
    required String phone,
    DateTime? dateOfBirth,
    bool isForeignNational = false,
  }) async =>
      RegisteredUser.fromJson(_object(await _client.post<dynamic>(
        '/api/v1/auth/register',
        body: {
          'email': email,
          'password': password,
          'fullName': fullName,
          'phone': phone,
          // A DATE, not an instant. Sending an ISO timestamp would let a time zone
          // shift somebody's birthday across midnight and change the age the
          // server computes from it.
          if (dateOfBirth != null)
            'dateOfBirth': _dateOnly(dateOfBirth),
          'isForeignNational': isForeignNational,
        },
        options: AuthInterceptor.anonymous(),
      )));

  /// Rotates the token pair.
  ///
  /// `anonymous` is LOAD-BEARING here, not tidiness: `AuthInterceptor` calls this
  /// from inside its own `onRequest`, and without the marker the rotation would
  /// enter that same `onRequest`, find the token stale, and await the very
  /// completer it is on its way to completing. `token_rotation_test` asserts this
  /// request goes out carrying no bearer at all.
  Future<AuthTokens> refresh(String refreshToken) async =>
      AuthTokens.fromJson(_object(await _client.post<dynamic>(
        '/api/v1/auth/refresh',
        body: {'refreshToken': refreshToken},
        options: AuthInterceptor.anonymous(),
      )));

  Future<void> signOut(String refreshToken, {bool allDevices = false}) =>
      _client.post<dynamic>(
        '/api/v1/auth/logout',
        body: {'refreshToken': refreshToken, 'allDevices': allDevices},
      );

  Future<AuthUser> me() async =>
      AuthUser.fromJson(_object(await _client.get<dynamic>('/api/v1/auth/me')));

  Future<void> resendVerification(String email) => _client.post<dynamic>(
        '/api/v1/auth/resend-verification',
        body: {'email': email},
        options: AuthInterceptor.anonymous(),
      );

  Future<void> verifyEmail(String token) => _client.post<dynamic>(
        '/api/v1/auth/verify-email',
        body: {'token': token},
        options: AuthInterceptor.anonymous(),
      );

  Future<void> forgotPassword(String email) => _client.post<dynamic>(
        '/api/v1/auth/forgot-password',
        body: {'email': email},
        options: AuthInterceptor.anonymous(),
      );

  Future<void> resetPassword(String token, String newPassword) =>
      _client.post<dynamic>(
        '/api/v1/auth/reset-password',
        body: {'token': token, 'newPassword': newPassword},
        options: AuthInterceptor.anonymous(),
      );

  /// Returns a FRESH token pair: changing a password revokes every other family
  /// and rotates the security stamp, so the old access token stops working on its
  /// very next request. The caller must install these immediately.
  Future<AuthTokens> changePassword(String current, String next) async =>
      AuthTokens.fromJson(_object(await _client.post<dynamic>(
        '/api/v1/auth/change-password',
        body: {'currentPassword': current, 'newPassword': next},
      )));

  Future<MySessions> sessions() async =>
      MySessions.fromJson(_object(await _client.get<dynamic>('/api/v1/auth/sessions')));

  Future<void> revokeSession(String familyId) =>
      _client.post<dynamic>('/api/v1/auth/sessions/$familyId/revoke');

  Future<AuthUser> updateProfile({
    required String fullName,
    required String phone,
  }) async =>
      AuthUser.fromJson(_object(await _client.put<dynamic>(
        '/api/v1/customers/me/profile',
        body: {'fullName': fullName, 'phone': phone},
      )));

  // ── Catalogue (anonymous) ───────────────────────────────────────────────────

  Future<Paged<CatalogueListing>> searchVehicles({
    String? cityId,
    String? carTypeId,
    String? dealerId,
    num? minDailyRate,
    num? maxDailyRate,
    String? transmission,
    int? minSeats,
    bool deliveryOnly = false,
    String? text,
    DateTime? pickupAt,
    DateTime? returnAt,
    int page = 1,
    int pageSize = 20,
    CancelToken? cancelToken,
  }) async =>
      Paged.fromJson(
        _object(await _client.get<dynamic>(
          '/api/v1/vehicles',
          query: {
            'cityId': cityId,
            'carTypeId': carTypeId,
            'dealerId': dealerId,
            'minDailyRate': minDailyRate,
            'maxDailyRate': maxDailyRate,
            'transmission': transmission,
            'minSeats': minSeats,
            // Sent only when true: the API reads a bare `false` fine, but leaving
            // it out keeps the URL readable in a log.
            if (deliveryOnly) 'deliveryOnly': true,
            'text': text,
            // Both or neither. Half a period is refused rather than ignored.
            'pickupAt': pickupAt?.toUtc().toIso8601String(),
            'returnAt': returnAt?.toUtc().toIso8601String(),
            'page': page,
            'pageSize': pageSize,
          },
          options: AuthInterceptor.anonymous(),
          cancelToken: cancelToken,
        )),
        CatalogueListing.fromJson,
      );

  /// The seat counts and car types the bookable catalogue holds: what the search's
  /// choices are built from, rather than from a list typed into the app.
  Future<CatalogueFacets> catalogueFacets() async => CatalogueFacets.fromJson(
        _object(await _client.get<dynamic>(
          '/api/v1/vehicles/facets',
          options: AuthInterceptor.anonymous(),
        )),
      );

  Future<CatalogueVehicle> vehicle(
    String vehicleId, {
    DateTime? pickupAt,
    DateTime? returnAt,
  }) async =>
      CatalogueVehicle.fromJson(_object(await _client.get<dynamic>(
        '/api/v1/vehicles/$vehicleId',
        query: {
          'pickupAt': pickupAt?.toUtc().toIso8601String(),
          'returnAt': returnAt?.toUtc().toIso8601String(),
        },
        options: AuthInterceptor.anonymous(),
      )));

  /// What a named rental would cost, priced by the SERVER.
  ///
  /// The app displays these figures and never computes its own. Requested before
  /// the booking button is shown, so a customer never sees a price beside a button
  /// that would be refused.
  Future<RentalQuote> quote({
    required String vehicleId,
    required DateTime pickupAt,
    required DateTime returnAt,
    required String pickupMethod,
    double? latitude,
    double? longitude,
  }) async =>
      RentalQuote.fromJson(_object(await _client.get<dynamic>(
        '/api/v1/vehicles/$vehicleId/quote',
        query: {
          'pickupAt': pickupAt.toUtc().toIso8601String(),
          'returnAt': returnAt.toUtc().toIso8601String(),
          'pickupMethod': pickupMethod,
          'latitude': latitude,
          'longitude': longitude,
        },
        options: AuthInterceptor.anonymous(),
      )));

  Future<PublicGalleryPage> gallery(String dealerId) async =>
      PublicGalleryPage.fromJson(_object(await _client.get<dynamic>(
        '/api/v1/galleries/$dealerId',
        options: AuthInterceptor.anonymous(),
      )));

  Future<Paged<GalleryReview>> galleryReviews(
    String dealerId, {
    int page = 1,
    int pageSize = 20,
  }) async =>
      Paged.fromJson(
        _object(await _client.get<dynamic>(
          '/api/v1/galleries/$dealerId/reviews',
          query: {'page': page, 'pageSize': pageSize},
          options: AuthInterceptor.anonymous(),
        )),
        GalleryReview.fromJson,
      );

  // ── Bookings ────────────────────────────────────────────────────────────────

  Future<Paged<BookingListItem>> myBookings({
    String? tab,
    int page = 1,
    int pageSize = 20,
  }) async =>
      Paged.fromJson(
        _object(await _client.get<dynamic>(
          '/api/v1/bookings',
          query: {'tab': tab, 'page': page, 'pageSize': pageSize},
        )),
        BookingListItem.fromJson,
      );

  Future<Map<String, int>> bookingTabCounts() async {
    final body = _object(await _client.get<dynamic>('/api/v1/bookings/tab-counts'));
    return body.map((key, value) => MapEntry(key, (value as num?)?.toInt() ?? 0));
  }

  /// The one booking the customer most needs to see, or null.
  ///
  /// Answers 200 with a null body when there is nothing live, which is the
  /// ordinary case for most people most of the time.
  Future<NextBooking?> nextBooking() async =>
      NextBooking.maybe(await _client.get<dynamic>('/api/v1/bookings/next'));

  Future<Booking> booking(String bookingId) async =>
      Booking.fromJson(_object(await _client.get<dynamic>('/api/v1/bookings/$bookingId')));

  /// Asks a gallery for a car. NO PRICES travel: every figure on the resulting
  /// booking is computed and frozen server-side, because a client that could name
  /// a total could name a cheaper one.
  Future<Booking> createBooking({
    required String vehicleId,
    required DateTime pickupAt,
    required DateTime returnAt,
    required String pickupMethod,
    double? latitude,
    double? longitude,
  }) async =>
      Booking.fromJson(_object(await _client.post<dynamic>(
        '/api/v1/bookings',
        body: {
          'vehicleId': vehicleId,
          'pickupAt': pickupAt.toUtc().toIso8601String(),
          'returnAt': returnAt.toUtc().toIso8601String(),
          'pickupMethod': pickupMethod,
          'latitude': latitude,
          'longitude': longitude,
        },
      )));

  /// Can answer with an EXPIRED booking rather than a cancelled one, when the
  /// window closed while the customer was deciding. The screen renders whatever
  /// status comes back rather than assuming the one it asked for.
  Future<Booking> cancelBooking(
    String bookingId, {
    required String reasonCode,
    String? details,
  }) async =>
      Booking.fromJson(_object(await _client.post<dynamic>(
        '/api/v1/bookings/$bookingId/cancel',
        body: {'reasonCode': reasonCode, 'details': details},
      )));

  /// Starts, resumes or replaces the checkout for this booking's deposit.
  ///
  /// Safe to repeat, and repeating is the intended way to recover: the server
  /// hands back the attempt already in flight rather than opening a second one,
  /// so a customer who closed the tab lands on the same card form. Answers 503
  /// `payments.provider_unavailable` while no provider is configured.
  Future<PaymentAttempt> openDepositCheckout(String bookingId) async =>
      PaymentAttempt.maybe(_object(await _client.post<dynamic>(
        '/api/v1/bookings/$bookingId/deposit-checkout',
      )))!;

  Future<Booking> reportNonDelivery(String bookingId, String details) async =>
      Booking.fromJson(_object(await _client.post<dynamic>(
        '/api/v1/bookings/$bookingId/report-non-delivery',
        body: {'details': details},
      )));

  // ── Shortlist ───────────────────────────────────────────────────────────────

  Future<List<SavedVehicle>> shortlist() async =>
      (await _client.get<List<dynamic>>('/api/v1/customers/me/shortlist'))
          .whereType<Map<String, dynamic>>()
          .map(SavedVehicle.fromJson)
          .toList();

  /// Which of these cars are already saved.
  ///
  /// Asked per page of results so a heart can be drawn without loading the whole
  /// list. It answers only about the ids NAMED, which is what keeps it from being
  /// a way to read a shortlist through a screen that was never shown one.
  Future<Set<String>> savedAmong(List<String> vehicleIds) async {
    if (vehicleIds.isEmpty) return <String>{};

    final response = await _client.get<List<dynamic>>(
      '/api/v1/customers/me/shortlist/membership',
      query: {'vehicleId': vehicleIds},
    );
    return response.map((entry) => '$entry').toSet();
  }

  /// Safe to repeat: saving what is already saved changes nothing and succeeds.
  Future<void> saveVehicle(String vehicleId) =>
      _client.put<dynamic>('/api/v1/customers/me/shortlist/$vehicleId');

  /// Safe to repeat, and works for a car that is no longer listed — which is
  /// precisely the entry somebody most wants gone.
  Future<void> forgetVehicle(String vehicleId) =>
      _client.delete<dynamic>('/api/v1/customers/me/shortlist/$vehicleId');

  // ── Documents ───────────────────────────────────────────────────────────────

  Future<CustomerDocuments> myDocuments() async => CustomerDocuments.fromJson(
        _object(await _client.get<dynamic>('/api/v1/customers/me/documents')),
      );

  Future<CustomerDocument> uploadDocument({
    required String type,
    required List<int> bytes,
    required String fileName,
    required String contentType,
  }) async {
    final form = FormData.fromMap({
      'type': type,
      'file': MultipartFile.fromBytes(
        bytes,
        filename: fileName,
        contentType: DioMediaType.parse(contentType),
      ),
    });

    return CustomerDocument.fromJson(_object(await _client.post<dynamic>(
      '/api/v1/customers/me/documents',
      body: form,
      options: ApiClient.upload(),
    )));
  }

  Future<SignedDocumentLink> documentLink(String documentId) async =>
      SignedDocumentLink.fromJson(_object(
        await _client.get<dynamic>('/api/v1/customers/me/documents/$documentId/link'),
      ));

  /// The document's BYTES, fetched by this app rather than handed to a browser.
  ///
  /// The download endpoint is protected twice on purpose -- the signature proves
  /// the link was minted here for this file and has not expired, and the bearer
  /// token proves there is still a live session behind the request. An external
  /// browser carries neither the session nor any way to get one, so handing it
  /// the URL produced a 401 and nothing else. This call goes through the SAME
  /// authenticated client every other request uses, so both checks are satisfied
  /// without loosening either.
  Future<DocumentBytes> documentBytes(String url) async {
    // `raw` rather than the wrapper, because this one response is bytes and not
    // JSON -- so the DioException has to be turned into an ApiFailure here, the
    // way the wrapper does for everything else. Without it a refused download
    // would reach the screen as a Dio type nothing there catches.
    try {
      final response = await _client.raw.get<List<int>>(
        url,
        options: Options(
          responseType: ResponseType.bytes,
          // The body is a PDF or a photograph, not the JSON every other call wants.
          headers: const {'Accept': '*/*'},
          receiveTimeout: const Duration(seconds: 60),
          validateStatus: (status) => status != null && status < 400,
        ),
      );

      return DocumentBytes(
        Uint8List.fromList(response.data ?? const <int>[]),
        // What the SERVER says it is. The app does not re-derive it from the
        // name: the storage key's extension is the platform's own record of
        // the type.
        response.headers.value('content-type')?.split(';').first.trim(),
      );
    } on DioException catch (error) {
      throw ApiFailure.from(error);
    }
  }

  // ── Disputes ────────────────────────────────────────────────────────────────

  Future<Dispute> dispute(String ticketId) async =>
      Dispute.fromJson(_object(await _client.get<dynamic>('/api/v1/disputes/$ticketId')));

  Future<Dispute> openDispute({
    required String bookingId,
    required String reason,
    List<String> evidenceKeys = const [],
  }) async =>
      Dispute.fromJson(_object(await _client.post<dynamic>(
        '/api/v1/disputes',
        body: {
          'bookingId': bookingId,
          'reason': reason,
          'evidenceKeys': evidenceKeys,
        },
      )));

  Future<Dispute> addDisputeStatement({
    required String ticketId,
    required String body,
    List<String> evidenceKeys = const [],
  }) async =>
      Dispute.fromJson(_object(await _client.post<dynamic>(
        '/api/v1/disputes/$ticketId/statements',
        body: {'body': body, 'evidenceKeys': evidenceKeys},
      )));

  Future<Dispute> withdrawDispute(String ticketId) async =>
      Dispute.fromJson(_object(
        await _client.post<dynamic>('/api/v1/disputes/$ticketId/withdraw'),
      ));

  Future<EvidenceUpload> requestEvidenceUpload({
    required String bookingId,
    required String fileName,
    required String contentType,
  }) async =>
      EvidenceUpload.fromJson(_object(await _client.post<dynamic>(
        '/api/v1/disputes/evidence/upload-url',
        body: {
          'bookingId': bookingId,
          'fileName': fileName,
          'contentType': contentType,
        },
      )));

  /// Step two: the bytes themselves, PUT to the one-shot URL the server minted.
  ///
  /// The body is the bytes, not a stream of them. A browser cannot send a streamed
  /// request body, and `Content-Length` is a header script is forbidden to set —
  /// so both are left to the transport, which knows the length of a byte list.
  Future<void> uploadEvidence({
    required String uploadUrl,
    required List<int> bytes,
    required String contentType,
  }) =>
      _client.putAbsolute<dynamic>(
        uploadUrl,
        body: Uint8List.fromList(bytes),
        options: ApiClient.upload(contentType: contentType),
      );

  // ── Notifications ───────────────────────────────────────────────────────────

  Future<NotificationFeed> notifications({int page = 1, int pageSize = 25}) async =>
      NotificationFeed.fromJson(_object(await _client.get<dynamic>(
        '/api/v1/notifications',
        query: {'page': page, 'pageSize': pageSize},
      )));

  Future<int> unreadNotificationCount() async {
    final value = await _client.get<dynamic>('/api/v1/notifications/unread-count');
    return value is num ? value.toInt() : 0;
  }

  Future<void> markNotificationRead(String notificationId) =>
      _client.post<dynamic>('/api/v1/notifications/$notificationId/read');

  Future<void> markAllNotificationsRead() =>
      _client.post<dynamic>('/api/v1/notifications/read-all');

  // ── Reviews ─────────────────────────────────────────────────────────────────

  Future<MyReview?> myReview(String bookingId) async => MyReview.maybe(
        await _client.get<dynamic>('/api/v1/bookings/$bookingId/review'),
      );

  /// What galleries are told about the caller.
  ///
  /// Aggregates only — a rating, five counts and an account age. No per-review
  /// rows and no dates on individual ratings, because a rating dated last Tuesday
  /// would tell a gallery when this customer rented from a competitor.
  Future<CustomerReputation> myReputation() async =>
      CustomerReputation.fromJson(_object(
        await _client.get<dynamic>('/api/v1/customers/me/reputation'),
      ));

  Future<MyReview> leaveReview({
    required String bookingId,
    required int rating,
    String? comment,
  }) async =>
      MyReview.maybe(await _client.post<dynamic>(
        '/api/v1/bookings/$bookingId/review',
        body: {'rating': rating, 'comment': comment},
      ))!;

  static String _dateOnly(DateTime value) =>
      '${value.year.toString().padLeft(4, '0')}-'
      '${value.month.toString().padLeft(2, '0')}-'
      '${value.day.toString().padLeft(2, '0')}';
}
