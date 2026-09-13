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

  static AppConfig fakeConfig() => AppConfig.fromJson(const {
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
        'vocabularies': <String, dynamic>{},
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
  Future<AppConfig> appConfig() async => fakeConfig();

  @override
  Future<List<Lookup>> cities() async => const [];

  @override
  Future<List<Lookup>> carTypes() async => const [];

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

  @override
  Future<Paged<BookingListItem>> myBookings({
    String? tab,
    int page = 1,
    int pageSize = 20,
  }) async =>
      const Paged(items: [], page: 1, pageSize: 20, totalCount: 0);

  @override
  Future<Map<String, int>> bookingTabCounts() async => const {};

  @override
  Future<NotificationFeed> notifications({int page = 1, int pageSize = 25}) async =>
      const NotificationFeed(
          items: [], page: 1, pageSize: 25, totalCount: 0, unreadCount: 0);

  @override
  Future<int> unreadNotificationCount() async => 0;

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
      const Paged(items: [], page: 1, pageSize: 20, totalCount: 0);

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
