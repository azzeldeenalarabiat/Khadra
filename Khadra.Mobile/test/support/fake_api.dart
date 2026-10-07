import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:khadra_mobile/api/dtos.dart';
import 'package:khadra_mobile/api/khadra_api.dart';
import 'package:khadra_mobile/core/api/api_client.dart';
import 'package:khadra_mobile/core/api/api_failure.dart';
import 'package:khadra_mobile/core/session/session_store.dart';

/// A [KhadraApi] that answers from memory.
///
/// It SUBCLASSES the real one rather than reimplementing an interface, so the day
/// an endpoint changes shape this stops compiling instead of quietly agreeing.
/// Anything not overridden still goes through Dio and will fail loudly, which is
/// the point: a test that reaches an endpoint it did not mean to should say so.
class FakeApi extends KhadraApi {
  FakeApi() : super(ApiClient(Dio()));

  int signOutCalls = 0;
  bool signOutAllDevices = false;

  /// When set, `signOut` fails the way the network does. The session must still
  /// end locally — see [SessionController.signOut].
  ApiFailure? signOutFailure;

  CustomerDocuments documents =
      const CustomerDocuments(documents: [], isComplete: true, missing: []);

  /// The `mobileApp` section `/app-config` answers with, or null for an API that
  /// predates it — which is also what every test that does not care gets.
  Map<String, dynamic>? mobileApp;

  /// The `payments.mode` `/app-config` answers with, or null to leave the section out.
  String? paymentsMode;

  /// The cancellation reasons `/app-config` publishes; none unless a test needs to cancel.
  List<Map<String, dynamic>> cancellationReasons = const [];

  static AppConfig fakeConfig({
    Map<String, dynamic>? mobileApp,
    String? paymentsMode,
    List<Map<String, dynamic>> cancellationReasons = const [],
  }) =>
      AppConfig.fromJson({
        'timeZone': 'Asia/Amman',
        'currency': {'code': 'JOD', 'minorUnits': 3},
        'maxAdvanceBookingDays': 180,
        'minimumBookingLeadTimeMinutes': 120,
        'maxRentalDays': 90,
        'paymentWindowHours': 24,
        'documents': {
          'maximumSizeBytes': 8388608,
          'allowedContentTypes': ['image/jpeg', 'image/png'],
        },
        'vocabularies': <String, dynamic>{
          if (cancellationReasons.isNotEmpty) 'cancellationReasons': cancellationReasons,
        },
        if (mobileApp != null) 'mobileApp': mobileApp,
        if (paymentsMode != null) 'payments': {'mode': paymentsMode},
      });

  static AuthUser fakeUser({
    String name = 'Layla Odeh',
    bool verified = true,
  }) =>
      AuthUser(
        id: '01a07e16-de7b-7673-b1a5-c47bc1d437f4',
        email: 'layla@example.jo',
        fullName: name,
        phone: '+962791234567',
        role: 'Customer',
        isEmailVerified: verified,
        mustChangePassword: false,
        createdAt: DateTime.utc(2026, 1, 5),
      );

  static AuthTokens fakeTokens() => AuthTokens(
        accessToken: 'access-token',
        accessTokenExpiresAt: DateTime.now().toUtc().add(const Duration(minutes: 15)),
        refreshToken: 'refresh-token',
        refreshTokenExpiresAt: DateTime.now().toUtc().add(const Duration(days: 14)),
        user: fakeUser(),
      );

  @override
  Future<AppConfig> appConfig() async => fakeConfig(
        mobileApp: mobileApp,
        paymentsMode: paymentsMode,
        cancellationReasons: cancellationReasons,
      );

  /// The lookups, empty unless a test says otherwise.
  List<Lookup> cityLookups = const [];
  List<Lookup> carTypeLookups = const [];

  @override
  Future<List<Lookup>> cities() async => cityLookups;

  @override
  Future<List<Lookup>> carTypes() async => carTypeLookups;

  /// What the catalogue holds. Empty unless a test says otherwise.
  CatalogueFacets facets = const CatalogueFacets(seats: [], carTypeIds: <String>{});

  /// When set, the facets endpoint fails this way — the way an older server
  /// without it answers.
  ApiFailure? facetsFailure;

  @override
  Future<CatalogueFacets> catalogueFacets() async {
    final failure = facetsFailure;
    if (failure != null) throw failure;
    return facets;
  }

  @override
  Future<AuthUser> me() async => fakeUser();

  /// The account the next `signIn` hands back. Settable because an UNVERIFIED one
  /// takes a different path off the form — it can sign in and still not book.
  AuthUser signInAs = fakeUser();
  int signInCalls = 0;
  ApiFailure? signInFailure;

  @override
  Future<AuthTokens> signIn(String email, String password) async {
    signInCalls++;
    final failure = signInFailure;
    if (failure != null) throw failure;

    final tokens = fakeTokens();
    return AuthTokens(
      accessToken: tokens.accessToken,
      accessTokenExpiresAt: tokens.accessTokenExpiresAt,
      refreshToken: tokens.refreshToken,
      refreshTokenExpiresAt: tokens.refreshTokenExpiresAt,
      user: signInAs,
    );
  }

  final List<String> verifiedTokens = <String>[];

  @override
  Future<void> verifyEmail(String token) async => verifiedTokens.add(token);

  /// Nothing needs the customer's attention unless a test says so. Null is the
  /// ordinary answer and the landing card renders nothing for it.
  NextBooking? next;

  @override
  Future<NextBooking?> nextBooking() async => next;

  @override
  Future<void> signOut(String refreshToken, {bool allDevices = false}) async {
    signOutCalls++;
    signOutAllDevices = allDevices;
    final failure = signOutFailure;
    if (failure != null) throw failure;
  }

  @override
  Future<CustomerDocuments> myDocuments() async => documents;

  // ── Profile ─────────────────────────────────────────────────────────────────

  String? updatedName;
  String? updatedPhone;

  /// What the server sends back. Deliberately settable: the phone comes home
  /// NORMALISED, and a screen that echoed what was typed would disagree with what
  /// the platform holds.
  AuthUser Function(String fullName, String phone)? onUpdateProfile;
  ApiFailure? updateProfileFailure;

  @override
  Future<AuthUser> updateProfile({
    required String fullName,
    required String phone,
  }) async {
    updatedName = fullName;
    updatedPhone = phone;
    final failure = updateProfileFailure;
    if (failure != null) throw failure;
    final build = onUpdateProfile;
    return build != null
        ? build(fullName, phone)
        : fakeUser(name: fullName);
  }

  /// What the list answers, whatever tab it was asked for. Empty unless a test
  /// says otherwise.
  Paged<BookingListItem> bookings =
      const Paged(items: [], page: 1, pageSize: 20, totalCount: 0);

  /// When set, `myBookings` waits on it instead of answering.
  ///
  /// It is how a test watches a fetch that is STILL RUNNING — which is the only
  /// moment the bookings screen's loading question has a real answer, and the
  /// moment a refresh used to blank the list.
  Completer<void>? holdBookings;

  /// When set, `myBookings` throws it. A reload that failed must replace the list
  /// rather than leave stale rows looking current.
  Object? bookingsFailure;

  int myBookingsCalls = 0;

  @override
  Future<Paged<BookingListItem>> myBookings({
    String? tab,
    int page = 1,
    int pageSize = 20,
  }) async {
    myBookingsCalls++;
    final hold = holdBookings;
    if (hold != null) await hold.future;
    final failure = bookingsFailure;
    if (failure != null) throw failure;
    return bookings;
  }

  Map<String, int> tabCounts = const {};

  int bookingTabCountsCalls = 0;

  @override
  Future<Map<String, int>> bookingTabCounts() async {
    bookingTabCountsCalls++;
    return tabCounts;
  }

  /// The one booking the detail endpoint answers with. Set by the detail tests.
  Booking? bookingById;

  /// How many times the detail endpoint was read. The checkout screen's polling is counted by it.
  int bookingReads = 0;

  @override
  Future<Booking> booking(String bookingId) async {
    bookingReads++;
    final found = bookingById;
    if (found == null) throw StateError("no booking was staged for $bookingId");
    return found;
  }

  /// The one dispute the detail endpoint answers with. Set by the dispute tests.
  Dispute? disputeById;

  @override
  Future<Dispute> dispute(String ticketId) async {
    final found = disputeById;
    if (found == null) throw StateError("no dispute was staged for $ticketId");
    return found;
  }

  /// Every ticket the app asked to withdraw, in order.
  final List<String> withdrawnTicketIds = [];

  @override
  Future<Dispute> withdrawDispute(String ticketId) async {
    withdrawnTicketIds.add(ticketId);
    final found = disputeById;
    if (found == null) throw StateError("no dispute was staged for $ticketId");
    return found;
  }

  /// The financial state the booking's financials endpoint answers with
  /// (payments Phase 4). Null by default: an API without Phase 4, whose 404 the
  /// app reads as "fall back to what the booking carries".
  BookingFinancials? financialsById;

  /// When set, the financials endpoint fails with it (a server error, not a 404).
  ApiFailure? financialsFailure;

  int financialsReads = 0;

  @override
  Future<BookingFinancials?> bookingFinancials(String bookingId) async {
    financialsReads++;
    final failure = financialsFailure;
    if (failure != null) throw failure;
    return financialsById;
  }

  // ── Issued financial documents (payments Phase 5b) ───────────────────────────

  /// The booking's documents. Null, the default, is an API without them: the
  /// screen shows nothing, which keeps every older booking test as it was.
  BookingFinancialDocuments? bookingDocumentsById;

  /// When set, the booking's documents fail with it.
  ApiFailure? bookingDocumentsFailure;

  int bookingDocumentsReads = 0;

  /// When set, a read of the booking's documents waits for it: how a test sees
  /// the block while "Check again" is still reading.
  Completer<void>? holdBookingDocuments;

  @override
  Future<BookingFinancialDocuments?> bookingFinancialDocuments(String bookingId) async {
    bookingDocumentsReads++;
    await holdBookingDocuments?.future;
    final failure = bookingDocumentsFailure;
    if (failure != null) throw failure;
    return bookingDocumentsById;
  }

  /// Invoices & Receipts, paged as the server pages them. (Not `myDocuments`: that
  /// is the customer's identity papers, here as everywhere.)
  List<FinancialDocumentRow> myFinancialDocumentRows = const [];

  /// The kind every list request asked for: null for every kind.
  final List<String?> myFinancialDocumentTypes = [];

  @override
  Future<Paged<FinancialDocumentRow>> myFinancialDocuments({String? type, int page = 1, int pageSize = 20}) async {
    myFinancialDocumentTypes.add(type);
    final matching = [for (final row in myFinancialDocumentRows) if (type == null || row.type == type) row];
    return Paged<FinancialDocumentRow>(
      items: matching.skip((page - 1) * pageSize).take(pageSize).toList(),
      page: page,
      pageSize: pageSize,
      totalCount: matching.length,
    );
  }

  /// One document by id. An id not here answers null, as the server's 404 does.
  Map<String, FinancialDocumentPage> documentsById = const {};

  @override
  Future<FinancialDocumentPage?> financialDocument(String documentId) async => documentsById[documentId];

  /// Every PDF link this phone asked for: (document, language).
  final List<(String, String)> pdfLinks = [];

  /// When set, the next PDF link is refused with it, once.
  ApiFailure? pdfLinkFailure;

  @override
  Future<SignedDocumentLink> financialDocumentPdfLink(String documentId, String language) async {
    pdfLinks.add((documentId, language));
    final failure = pdfLinkFailure;
    if (failure != null) {
      pdfLinkFailure = null;
      throw failure;
    }
    return SignedDocumentLink('https://api.test/api/v1/documents/signed-$language?expires=1&signature=s', null);
  }

  /// Every private file this phone fetched, by its signed address.
  final List<String> fetchedFiles = [];

  @override
  Future<DocumentBytes> documentBytes(String url) async {
    fetchedFiles.add(url);
    return DocumentBytes(Uint8List.fromList(utf8.encode('%PDF-1.7')), 'application/pdf');
  }

  /// Every cancellation this phone sent: (reason code, details, expected refund).
  final List<(String, String?, num?)> cancellations = [];

  /// When set, the next cancellation is refused with it, once.
  ApiFailure? cancelFailure;

  @override
  Future<Booking> cancelBooking(
    String bookingId, {
    required String reasonCode,
    String? details,
    num? expectedRefund,
  }) async {
    cancellations.add((reasonCode, details, expectedRefund));
    final failure = cancelFailure;
    if (failure != null) {
      cancelFailure = null;
      throw failure;
    }
    final found = bookingById;
    if (found == null) throw StateError("no booking was staged for $bookingId");
    return found;
  }

  /// Every push registration this phone sent, in order: (token, language).
  final List<(String, String)> pushRegistrations = [];
  int pushRemovals = 0;
  final List<String> languagesSet = [];

  /// When set, the push calls fail the way the network does.
  ApiFailure? pushFailure;

  @override
  Future<void> registerPushDevice({
    required String token,
    required String platform,
    required String language,
    String? appVersion,
  }) async {
    if (pushFailure != null) throw pushFailure!;
    pushRegistrations.add((token, language));
  }

  @override
  Future<void> removePushDevice() async {
    if (pushFailure != null) throw pushFailure!;
    pushRemovals++;
  }

  @override
  Future<void> setLanguage(String language) async {
    if (pushFailure != null) throw pushFailure!;
    languagesSet.add(language);
  }

  /// What `handover-code` answers with, one per call; the last one repeats.
  List<HandoverCodeGrant> handoverGrants = [];
  int handoverCalls = 0;

  @override
  Future<HandoverCodeGrant> issueHandoverCode(String bookingId) async {
    handoverCalls++;
    if (handoverGrants.isEmpty) throw StateError("no handover code was staged for $bookingId");
    return handoverGrants[(handoverCalls - 1).clamp(0, handoverGrants.length - 1)];
  }

  /// What `deposit-checkout` answers with. Null fails the call loudly, like any unstaged endpoint.
  PaymentAttempt? checkoutAttempt;
  int checkoutCalls = 0;

  @override
  Future<PaymentAttempt> openDepositCheckout(String bookingId) async {
    checkoutCalls++;
    final attempt = checkoutAttempt;
    if (attempt == null) throw StateError('no checkout was staged for $bookingId');
    return attempt;
  }

  /// The purposes `checkout` was asked for, in order: a test checks the app
  /// names a PURPOSE and never an amount.
  final List<String> checkoutPurposes = [];

  @override
  Future<PaymentAttempt> openCheckout(String bookingId, String purpose) async {
    checkoutPurposes.add(purpose);
    return openDepositCheckout(bookingId);
  }

  /// What the Alerts tab reads. Empty unless a test says otherwise.
  NotificationFeed notificationFeed = const NotificationFeed(
      items: [], page: 1, pageSize: 25, totalCount: 0, unreadCount: 0);

  /// The notifications marked read, in order.
  final List<String> markedRead = [];

  @override
  Future<NotificationFeed> notifications({int page = 1, int pageSize = 25}) async =>
      notificationFeed;

  @override
  Future<void> markNotificationRead(String notificationId) async =>
      markedRead.add(notificationId);

  /// Every upload, as (type, file name, content type).
  final List<(String, String, String)> uploads = [];

  /// What the documents read answers after an upload. Unchanged when null.
  CustomerDocuments Function(String type)? afterUpload;

  @override
  Future<CustomerDocument> uploadDocument({
    required String type,
    required List<int> bytes,
    required String fileName,
    required String contentType,
  }) async {
    uploads.add((type, fileName, contentType));
    final next = afterUpload?.call(type);
    if (next != null) documents = next;
    return documents.ofType(type) ??
        CustomerDocument(
          documentId: 'uploaded-$type',
          type: type,
          status: 'PendingReview',
          contentType: contentType,
          sizeBytes: bytes.length,
          uploadedAt: DateTime.utc(2026, 10, 7, 12),
          reviewNote: null,
        );
  }

  @override
  Future<int> unreadNotificationCount() async => 0;

  /// What a search answers, whatever it asked. Empty unless a test says otherwise.
  Paged<CatalogueListing> searchResult =
      const Paged(items: [], page: 1, pageSize: 20, totalCount: 0);

  @override
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
      searchResult;

  // ── A rental office's page ──────────────────────────────────────────────────

  /// The page a dealer id answers with. Set by the test; reaching it unset is a
  /// test asking for a page it never described.
  PublicGalleryPage? galleryPage;

  /// That office's reviews. Empty unless a test says otherwise.
  Paged<GalleryReview> galleryReviewPage =
      const Paged(items: [], page: 1, pageSize: 20, totalCount: 0);

  @override
  Future<PublicGalleryPage> gallery(String dealerId) async =>
      galleryPage ?? (throw StateError('no gallery page set for $dealerId'));

  @override
  Future<Paged<GalleryReview>> galleryReviews(
    String dealerId, {
    int page = 1,
    int pageSize = 20,
  }) async =>
      galleryReviewPage;

  // ── Saved cars ──────────────────────────────────────────────────────────────

  /// The saved LIST, which is a different question from the membership set a
  /// heart asks — hence the name. `shortlist_test.dart` subclasses this with its
  /// own `saved` set for the heart.
  List<SavedVehicle> savedCars = const [];

  final List<String> forgotten = <String>[];

  @override
  Future<List<SavedVehicle>> shortlist() async => savedCars;

  @override
  Future<Set<String>> savedAmong(List<String> vehicleIds) async => savedCars
      .map((entry) => entry.vehicleId)
      .toSet()
      .intersection(vehicleIds.toSet());

  @override
  Future<void> forgetVehicle(String vehicleId) async {
    forgotten.add(vehicleId);
    savedCars = [
      for (final entry in savedCars)
        if (entry.vehicleId != vehicleId) entry,
    ];
  }

  // ── Registered devices ──────────────────────────────────────────────────────

  /// What `/auth/sessions` answers. Empty by default, because most screens never
  /// ask; the devices screen sets its own.
  MySessions mySessions = const MySessions(<SessionSummary>[], 15);

  final List<String> revokedFamilies = <String>[];

  @override
  Future<MySessions> sessions() async => mySessions;

  @override
  Future<void> revokeSession(String familyId) async => revokedFamilies.add(familyId);
}

/// The token store, in memory.
///
/// It keeps the real one's OWNERSHIP rule rather than only its storage: a token is
/// readable when this install claims it and invisible when it does not. That is
/// what makes a sign-out stick even when a delete fails, so a fake that ignored it
/// would let a test pass on a session the app could not actually end.
class FakeSessionStore extends SessionStore {
  FakeSessionStore({this.refreshToken, this.owned = true});

  String? refreshToken;
  DateTime? refreshExpiry;
  int clearCalls = 0;

  /// Whether this install claims the token below. See `SessionStore.sessionIsOwned`.
  bool owned;

  /// Set behind the store's back, the way a Keychain entry survives an app being
  /// deleted or a failed delete leaves one behind.
  void plantDisownedToken(String token, DateTime expiresAt) {
    refreshToken = token;
    refreshExpiry = expiresAt.toUtc();
    owned = false;
  }

  @override
  bool get sessionIsOwned => owned;

  @override
  Future<void> discardDisownedTokens() async {
    if (owned) return;
    await clear();
  }

  @override
  Future<void> disownSession() async => owned = false;

  /// When true the store stops ANSWERING, the way a locked keystore does: reads
  /// come back null without that meaning the token is gone.
  bool unreadable = false;

  // `readRefreshToken` is deliberately NOT overridden -- the real one delegates to
  // this, so a test that fakes only the outcome cannot have the two disagree.
  @override
  Future<({String? token, bool answered})> readRefreshTokenOutcome() async =>
      unreadable
          ? (token: null, answered: false)
          : (token: owned ? refreshToken : null, answered: true);

  @override
  Future<DateTime?> readRefreshExpiry() async => owned ? refreshExpiry : null;

  @override
  Future<void> saveRefreshToken(String token, DateTime expiresAt) async {
    refreshToken = token;
    refreshExpiry = expiresAt.toUtc();
    owned = true;
  }

  @override
  Future<void> clear() async {
    clearCalls++;
    clearAccessToken();
    owned = false;
    refreshToken = null;
    refreshExpiry = null;
  }
}
