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
}

/// A [SessionStore] backed by two fields, so no test touches a real keystore.
class FakeSessionStore extends SessionStore {
  FakeSessionStore({this.refreshToken});

  String? refreshToken;
  DateTime? refreshExpiry;
  int clearCalls = 0;

  @override
  Future<void> clearIfReinstalled() async {}

  @override
  Future<String?> readRefreshToken() async => refreshToken;

  @override
  Future<DateTime?> readRefreshExpiry() async => refreshExpiry;

  @override
  Future<void> saveRefreshToken(String token, DateTime expiresAt) async {
    refreshToken = token;
    refreshExpiry = expiresAt.toUtc();
  }

  @override
  Future<void> clear() async {
    clearCalls++;
    clearAccessToken();
    refreshToken = null;
    refreshExpiry = null;
  }
}
