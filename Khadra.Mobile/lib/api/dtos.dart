// Field-for-field mirrors of the server's records. No logic lives here beyond
// reading JSON: every figure on these types was computed by the server and the app
// renders it unchanged.
//
// Money arrives as a JSON number and is kept as `num`. The app does no arithmetic
// on it at all, and displays it only through `Formats.money`, which pads to the
// minor units `/app-config` names -- three for the dinar, where every developer's
// instinct is two, and where guessing renders 12.75 against a contract that says
// 12.750.

import 'dart:typed_data';

import '../core/config/app_environment.dart';
import '../core/config/app_version.dart';
import '../core/format/booking_presentation.dart' show HasDealerLabel;

int _int(dynamic value, [int fallback = 0]) => switch (value) {
      int v => v,
      num v => v.toInt(),
      String v => int.tryParse(v) ?? fallback,
      _ => fallback,
    };

num _num(dynamic value, [num fallback = 0]) => switch (value) {
      num v => v,
      String v => num.tryParse(v) ?? fallback,
      _ => fallback,
    };

DateTime? _dateTime(dynamic value) =>
    value is String ? DateTime.tryParse(value)?.toUtc() : null;

DateTime _requiredDateTime(dynamic value) =>
    _dateTime(value) ?? DateTime.fromMillisecondsSinceEpoch(0, isUtc: true);

/// An amount and the currency it is in. The code travels with every figure so no
/// screen ever writes "JOD" as a literal beside a number that might not be one.
class Money {
  const Money(this.amount, this.currencyCode);

  final num amount;
  final String currencyCode;

  /// The field is `currency`: that is what `MoneyDto` on the server is named.
  ///
  /// Reading the wrong key would fall through to a default, and a DEFAULT
  /// CURRENCY is the exact mistake this type exists to prevent — the app would
  /// print "JOD" beside an amount in something else, and be right only until the
  /// day it was not. So there is no default: an amount with no currency on it
  /// renders with none, which is visibly wrong rather than quietly wrong.
  static Money fromJson(Map<String, dynamic> json) =>
      Money(_num(json['amount']), json['currency'] as String? ?? '');

  static Money? maybe(dynamic json) =>
      json is Map<String, dynamic> ? fromJson(json) : null;

  bool get isZero => amount == 0;
}

class GeoPoint {
  const GeoPoint(this.latitude, this.longitude);

  final double latitude;
  final double longitude;

  static GeoPoint? maybe(dynamic json) => json is Map<String, dynamic>
      ? GeoPoint(
          (_num(json['latitude'])).toDouble(),
          (_num(json['longitude'])).toDouble(),
        )
      : null;
}

class Paged<T> {
  const Paged({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.totalCount,
  });

  final List<T> items;
  final int page;
  final int pageSize;
  final int totalCount;

  int get totalPages => pageSize <= 0 ? 0 : (totalCount / pageSize).ceil();
  bool get hasNext => page < totalPages;

  static Paged<T> fromJson<T>(
    Map<String, dynamic> json,
    T Function(Map<String, dynamic>) item,
  ) =>
      Paged<T>(
        items: (json['items'] as List<dynamic>? ?? const [])
            .whereType<Map<String, dynamic>>()
            .map(item)
            .toList(),
        page: _int(json['page'], 1),
        pageSize: _int(json['pageSize'], 20),
        totalCount: _int(json['totalCount']),
      );

  static Paged<T> empty<T>() =>
      Paged<T>(items: const [], page: 1, pageSize: 20, totalCount: 0);
}

// ── Platform configuration ─────────────────────────────────────────────────────

/// Everything the app would otherwise have to hard-code, which for a phone means
/// a new release in every store each time the owner changes a number.
class AppConfig {
  const AppConfig({
    required this.timeZone,
    required this.currency,
    required this.minimumRenterAge,
    required this.maxAdvanceBookingDays,
    required this.minimumBookingLeadTimeMinutes,
    required this.maxRentalDays,
    required this.documents,
    required this.password,
    required this.payments,
    required this.vocabularies,
    this.mobileApp = MobileAppConfig.none,
  });

  /// The IANA zone every calendar answer on this platform is expressed in. The
  /// date picker runs in it, because rentals are billed in Amman calendar days and
  /// a picker in the device's zone would price a different number of them.
  final String timeZone;
  final CurrencyConfig currency;

  /// Null means the owner has set no age limit and nobody is refused on age — a
  /// real shipping state, not a missing value. The registration form asks for a
  /// date of birth exactly when this is non-null.
  final int? minimumRenterAge;
  final int maxAdvanceBookingDays;
  final int minimumBookingLeadTimeMinutes;
  final int maxRentalDays;
  final DocumentLimits documents;

  /// What makes a password acceptable, so the app states the platform's rule
  /// rather than a copy of it. Null when this server is older than the field, or
  /// when the config has not arrived — in which case the app describes no rule at
  /// all and lets the server judge, which is the only honest fallback.
  final PasswordPolicy? password;

  /// What kind of money this deployment moves.
  ///
  /// Nothing else the app is told can answer it. A booking says nothing about it,
  /// and `canPay` answers "will the button work", which is a different question
  /// from "is any of this real". The test banner keys on this and only this.
  final PaymentsConfig payments;

  /// Which builds of this app the API still serves. See [MobileAppConfig].
  final MobileAppConfig mobileApp;

  final Vocabularies vocabularies;

  static AppConfig fromJson(Map<String, dynamic> json) => AppConfig(
        timeZone: json['timeZone'] as String? ?? 'Asia/Amman',
        currency: CurrencyConfig.fromJson(
            json['currency'] as Map<String, dynamic>? ?? const {}),
        minimumRenterAge: json['minimumRenterAge'] as int?,
        maxAdvanceBookingDays: _int(json['maxAdvanceBookingDays'], 180),
        minimumBookingLeadTimeMinutes:
            _int(json['minimumBookingLeadTimeMinutes'], 120),
        maxRentalDays: _int(json['maxRentalDays'], 90),
        documents: DocumentLimits.fromJson(
            json['documents'] as Map<String, dynamic>? ?? const {}),
        // NO default. Every other field here falls back to the shipped figure,
        // which is right for a bound the picker cannot open without — but a
        // password rule invented on the phone is the exact drift this field
        // exists to end, and `?? 8` would reintroduce it wearing a different hat.
        password: PasswordPolicy.maybe(json['password']),
        payments: PaymentsConfig.fromJson(json['payments']),
        vocabularies: Vocabularies.fromJson(
            json['vocabularies'] as Map<String, dynamic>? ?? const {}),
        mobileApp: MobileAppConfig.fromJson(json['mobileApp']),
      );
}

/// What kind of money this deployment moves: `None`, `Sandbox` or `Live`.
///
/// One field, and the app's rule is "show the test banner when it reads Sandbox,
/// and not otherwise". A second derived flag beside it would be two encodings of
/// one fact, and the failure that enables is the expensive direction: telling a
/// paying customer their payment was fake is far worse than missing a banner on a
/// test build.
///
/// **Unknown is not Sandbox.** An older server, a field that never arrived, a
/// value this release has never heard of — all of them mean the app says nothing,
/// because a banner shown wrongly is worse than one missed. The server is where
/// the truth about money lives, and it says so plainly or it says nothing.
class PaymentsConfig {
  const PaymentsConfig(this.mode);

  /// Exactly as the server wrote it. Compared case-insensitively, never parsed
  /// into an enum the app would then have to grow a case for.
  final String mode;

  /// The one thing any screen asks. Everything else is "not sandbox".
  bool get isSandbox => mode.toLowerCase() == 'sandbox';

  static PaymentsConfig fromJson(dynamic value) => PaymentsConfig(
        value is Map<String, dynamic> ? value['mode'] as String? ?? '' : '',
      );
}

/// Which builds of this app the API still serves, and where to get a newer one.
///
/// **Absent means "nothing is refused"**, deliberately, the way a missing
/// `password` means "let the server judge": an API that predates this field is the
/// one a 1.1.0 build meets if it is installed before the API is switched, and that
/// build must work against it rather than lock itself out.
///
/// The minimum is compared with the installed version by [AppVersion] — never as
/// text. When either side cannot be read, the app defers to the server, which
/// refuses an unsupported build with 426 on every call regardless.
class MobileAppConfig {
  const MobileAppConfig({this.minimumSupportedVersion, this.updateUrl});

  static const none = MobileAppConfig();

  final AppVersion? minimumSupportedVersion;

  /// Where the current build can be downloaded. Null when none has been
  /// published — the update screen then says where to look instead of inventing
  /// a link.
  final Uri? updateUrl;

  static MobileAppConfig fromJson(dynamic json) {
    if (json is! Map<String, dynamic>) return none;
    final link = Uri.tryParse(json['updateUrl'] as String? ?? '');
    return MobileAppConfig(
      minimumSupportedVersion:
          AppVersion.tryParse(json['minimumSupportedVersion'] as String?),
      updateUrl: link != null && (link.scheme == 'https' || link.scheme == 'http')
          ? link
          : null,
    );
  }
}

/// What the platform will accept as a password.
///
/// The app used to hold this itself — a minimum of 8 and the sentence "with a
/// letter and a number" — while the server's minimum is configurable from 8 to
/// 64. The day the owner raised it, every installed phone would have gone on
/// promising 8, accepting a 9-character password locally, and showing the
/// server's English refusal on the registration screen.
///
/// The SENTENCE is still the app's, composed from these flags, because "at least
/// {n} characters" in Arabic needs plural forms that a C# interpolation cannot
/// produce. The RULE is the server's.
class PasswordPolicy {
  const PasswordPolicy({
    required this.minimumLength,
    required this.maximumLength,
    required this.requiresLetter,
    required this.requiresDigit,
    required this.allowsWhitespace,
  });

  final int minimumLength;

  /// bcrypt's input cap, not a policy choice.
  final int maximumLength;

  final bool requiresLetter;
  final bool requiresDigit;
  final bool allowsWhitespace;

  static PasswordPolicy? maybe(dynamic json) {
    if (json is! Map<String, dynamic>) return null;
    final minimum = json['minimumLength'];
    if (minimum is! num) return null;

    return PasswordPolicy(
      minimumLength: minimum.toInt(),
      maximumLength: _int(json['maximumLength'], 72),
      requiresLetter: json['requiresLetter'] as bool? ?? false,
      requiresDigit: json['requiresDigit'] as bool? ?? false,
      allowsWhitespace: json['allowsWhitespace'] as bool? ?? true,
    );
  }
}

/// Sent rather than assumed because the dinar has THREE decimals.
class CurrencyConfig {
  const CurrencyConfig(this.code, this.minorUnits);

  final String code;
  final int minorUnits;

  static CurrencyConfig fromJson(Map<String, dynamic> json) => CurrencyConfig(
        json['code'] as String? ?? 'JOD',
        _int(json['minorUnits'], 3),
      );
}

class DocumentLimits {
  const DocumentLimits(this.maximumSizeBytes, this.allowedContentTypes);

  final int maximumSizeBytes;
  final List<String> allowedContentTypes;

  static DocumentLimits fromJson(Map<String, dynamic> json) => DocumentLimits(
        _int(json['maximumSizeBytes'], 8 * 1024 * 1024),
        (json['allowedContentTypes'] as List<dynamic>? ?? const [])
            .map((entry) => '$entry')
            .toList(),
      );
}

/// The closed sets a customer chooses from, in both languages.
///
/// The NAME is the contract and is what gets sent back; the label is what the
/// screen shows. Keeping both is what lets a chip's text be the platform's word
/// rather than a literal in a widget — the standing rule against static data.
class Vocabularies {
  const Vocabularies({
    required this.transmissions,
    required this.fuelTypes,
    required this.pickupMethods,
    required this.cancellationReasons,
    required this.rejectionReasons,
  });

  final List<VocabularyEntry> transmissions;
  final List<VocabularyEntry> fuelTypes;
  final List<VocabularyEntry> pickupMethods;
  final List<VocabularyEntry> cancellationReasons;
  final List<VocabularyEntry> rejectionReasons;

  static List<VocabularyEntry> _list(dynamic value) =>
      (value as List<dynamic>? ?? const [])
          .whereType<Map<String, dynamic>>()
          .map(VocabularyEntry.fromJson)
          .toList();

  static Vocabularies fromJson(Map<String, dynamic> json) => Vocabularies(
        transmissions: _list(json['transmissions']),
        fuelTypes: _list(json['fuelTypes']),
        pickupMethods: _list(json['pickupMethods']),
        cancellationReasons: _list(json['cancellationReasons']),
        rejectionReasons: _list(json['rejectionReasons']),
      );

  /// The label for one member, in the reader's language, or the name itself for a
  /// member this version of the platform did not publish.
  static String label(List<VocabularyEntry> entries, String? name, bool arabic) {
    if (name == null) return '';
    for (final entry in entries) {
      if (entry.name == name) return arabic ? entry.labelAr : entry.labelEn;
    }
    return name;
  }
}

class VocabularyEntry {
  const VocabularyEntry(this.name, this.labelEn, this.labelAr);

  final String name;
  final String labelEn;
  final String labelAr;

  static VocabularyEntry fromJson(Map<String, dynamic> json) => VocabularyEntry(
        json['name'] as String? ?? '',
        json['labelEn'] as String? ?? '',
        json['labelAr'] as String? ?? '',
      );

  String labelFor(bool arabic) => arabic ? labelAr : labelEn;
}

/// A bilingual lookup row — a city, a car type.
class Lookup {
  const Lookup({
    required this.id,
    required this.nameEn,
    required this.nameAr,
    required this.isActive,
  });

  final String id;
  final String nameEn;
  final String nameAr;
  final bool isActive;

  static Lookup fromJson(Map<String, dynamic> json) => Lookup(
        id: json['id'] as String? ?? '',
        nameEn: json['nameEn'] as String? ?? '',
        nameAr: json['nameAr'] as String? ?? '',
        isActive: json['isActive'] as bool? ?? true,
      );

  String nameFor(bool arabic) {
    final name = arabic ? nameAr : nameEn;
    return name.isEmpty ? (arabic ? nameEn : nameAr) : name;
  }
}

/// What the bookable catalogue holds, which the search's choices are built from.
///
/// Seat counts ascending, and car type IDS only: the names are the lookup's, in both
/// languages. A category is offered when it is in both, so one with no bookable car
/// offers no chip, and neither does one an administrator has retired.
class CatalogueFacets {
  const CatalogueFacets({required this.seats, required this.carTypeIds});

  final List<int> seats;
  final Set<String> carTypeIds;

  static CatalogueFacets fromJson(Map<String, dynamic> json) => CatalogueFacets(
        seats: [
          for (final value in json['seats'] as List<dynamic>? ?? const [])
            if (value is num) value.toInt(),
        ],
        carTypeIds: {
          for (final value in json['carTypeIds'] as List<dynamic>? ?? const [])
            if (value is String) value,
        },
      );
}

// ── Identity ───────────────────────────────────────────────────────────────────

class AuthUser {
  const AuthUser({
    required this.id,
    required this.email,
    required this.fullName,
    required this.phone,
    required this.role,
    required this.isEmailVerified,
    required this.mustChangePassword,
    required this.createdAt,
  });

  final String id;
  final String email;
  final String fullName;
  final String phone;
  final String role;
  final bool isEmailVerified;
  final bool mustChangePassword;
  final DateTime createdAt;

  bool get isCustomer => role == 'Customer';

  static AuthUser fromJson(Map<String, dynamic> json) => AuthUser(
        id: json['id'] as String? ?? '',
        email: json['email'] as String? ?? '',
        fullName: json['fullName'] as String? ?? '',
        phone: json['phone'] as String? ?? '',
        role: json['role'] as String? ?? '',
        isEmailVerified: json['isEmailVerified'] as bool? ?? false,
        mustChangePassword: json['mustChangePassword'] as bool? ?? false,
        createdAt: _requiredDateTime(json['createdAt']),
      );
}

class AuthTokens {
  const AuthTokens({
    required this.accessToken,
    required this.accessTokenExpiresAt,
    required this.refreshToken,
    required this.refreshTokenExpiresAt,
    required this.user,
  });

  final String accessToken;
  final DateTime accessTokenExpiresAt;
  final String refreshToken;
  final DateTime refreshTokenExpiresAt;
  final AuthUser user;

  static AuthTokens fromJson(Map<String, dynamic> json) => AuthTokens(
        accessToken: json['accessToken'] as String? ?? '',
        accessTokenExpiresAt: _requiredDateTime(json['accessTokenExpiresAt']),
        refreshToken: json['refreshToken'] as String? ?? '',
        refreshTokenExpiresAt: _requiredDateTime(json['refreshTokenExpiresAt']),
        user: AuthUser.fromJson(json['user'] as Map<String, dynamic>? ?? const {}),
      );
}

class RegisteredUser {
  const RegisteredUser({
    required this.userId,
    required this.email,
    required this.verificationEmailSent,
  });

  final String userId;
  final String email;

  /// False does NOT mean registration failed — the account and its token are saved
  /// either way. It means nothing is on its way to that inbox, so the person
  /// should be told to ask for another link rather than sent off to wait.
  final bool verificationEmailSent;

  static RegisteredUser fromJson(Map<String, dynamic> json) => RegisteredUser(
        userId: json['userId'] as String? ?? '',
        email: json['email'] as String? ?? '',
        verificationEmailSent: json['verificationEmailSent'] as bool? ?? false,
      );
}

/// One SIGN-IN, not one token refresh: the server groups by refresh-token family,
/// so a device that has rotated its token a hundred times is still one row.
class SessionSummary {
  const SessionSummary({
    required this.familyId,
    required this.signedInAt,
    required this.lastUsedAt,
    required this.expiresAt,
    required this.createdByIp,
    required this.userAgent,
    required this.isActive,
    required this.isCurrent,
  });

  final String familyId;
  final DateTime signedInAt;
  final DateTime lastUsedAt;
  final DateTime expiresAt;
  final String? createdByIp;
  final String? userAgent;
  final bool isActive;

  /// Whether this is the phone in the customer's hand.
  ///
  /// The SERVER says so, from the session id in the access token this request carried — the app
  /// cannot work it out, and must not try: nothing here is read out of the JWT, and the obvious
  /// guess (the most recently used row) is wrong, because "last used" is the last token refresh
  /// and another device may have rotated more recently.
  ///
  /// False also means "this build of the server could not say", which is the case for a token
  /// minted before the claim existed. So a row is marked only when this is true; nothing is
  /// inferred from its absence.
  final bool isCurrent;

  static SessionSummary fromJson(Map<String, dynamic> json) => SessionSummary(
        familyId: json['familyId'] as String? ?? '',
        signedInAt: _requiredDateTime(json['signedInAt']),
        lastUsedAt: _requiredDateTime(json['lastUsedAt']),
        expiresAt: _requiredDateTime(json['expiresAt']),
        createdByIp: json['createdByIp'] as String?,
        userAgent: json['userAgent'] as String?,
        isActive: json['isActive'] as bool? ?? true,
        isCurrent: json['isCurrent'] as bool? ?? false,
      );
}

class MySessions {
  const MySessions(this.sessions, this.accessTokenMinutes);

  final List<SessionSummary> sessions;
  final int accessTokenMinutes;

  static MySessions fromJson(Map<String, dynamic> json) => MySessions(
        (json['sessions'] as List<dynamic>? ?? const [])
            .whereType<Map<String, dynamic>>()
            .map(SessionSummary.fromJson)
            .toList(),
        _int(json['accessTokenMinutes'], 15),
      );
}

// ── Documents ──────────────────────────────────────────────────────────────────

class CustomerDocument {
  const CustomerDocument({
    required this.documentId,
    required this.type,
    required this.status,
    required this.contentType,
    required this.sizeBytes,
    required this.uploadedAt,
    required this.reviewNote,
  });

  final String documentId;
  final String type;
  final String status;

  /// What the server actually stored. Empty on a server that predates the field,
  /// which the tile renders as nothing rather than as a guess.
  final String contentType;
  final int sizeBytes;
  final DateTime uploadedAt;
  final String? reviewNote;

  static CustomerDocument fromJson(Map<String, dynamic> json) => CustomerDocument(
        documentId: json['documentId'] as String? ?? '',
        type: json['type'] as String? ?? '',
        status: json['status'] as String? ?? '',
        contentType: json['contentType'] as String? ?? '',
        sizeBytes: _int(json['sizeBytes']),
        uploadedAt: _requiredDateTime(json['uploadedAt']),
        reviewNote: json['reviewNote'] as String?,
      );
}

/// The checklist, so the app can tell a customer what is still missing BEFORE they
/// reach a booking button that would refuse them.
class CustomerDocuments {
  const CustomerDocuments({
    required this.documents,
    required this.isComplete,
    required this.missing,
  });

  final List<CustomerDocument> documents;
  final bool isComplete;
  final List<String> missing;

  static CustomerDocuments fromJson(Map<String, dynamic> json) =>
      CustomerDocuments(
        documents: (json['documents'] as List<dynamic>? ?? const [])
            .whereType<Map<String, dynamic>>()
            .map(CustomerDocument.fromJson)
            .toList(),
        isComplete: json['isComplete'] as bool? ?? false,
        missing: (json['missing'] as List<dynamic>? ?? const [])
            .map((entry) => '$entry')
            .toList(),
      );

  CustomerDocument? ofType(String type) {
    for (final document in documents) {
      if (document.type == type) return document;
    }
    return null;
  }
}

/// A document's bytes, and what the server said they are.
class DocumentBytes {
  const DocumentBytes(this.bytes, this.contentType);

  final Uint8List bytes;

  /// From the response header. Null when the server did not say, which is the
  /// case the caller has to name a file for anyway.
  final String? contentType;
}

class SignedDocumentLink {
  const SignedDocumentLink(this.url, this.expiresAt);

  final String url;
  final DateTime? expiresAt;

  static SignedDocumentLink fromJson(Map<String, dynamic> json) =>
      SignedDocumentLink(
        AppEnvironment.resolve(json['url'] as String? ?? ''),
        _dateTime(json['expiresAt']),
      );
}

// ── Catalogue ──────────────────────────────────────────────────────────────────

class CatalogueCarType {
  const CatalogueCarType(this.carTypeId, this.nameEn, this.nameAr);

  final String carTypeId;
  final String nameEn;
  final String nameAr;

  static CatalogueCarType? maybe(dynamic json) => json is Map<String, dynamic>
      ? CatalogueCarType(
          json['carTypeId'] as String? ?? '',
          json['nameEn'] as String? ?? '',
          json['nameAr'] as String? ?? '',
        )
      : null;

  String nameFor(bool arabic) => arabic ? nameAr : nameEn;
}

/// The gallery behind a search row: enough to recognise it, nothing more.
///
/// The rating is the GALLERY's and sits here rather than on the car deliberately —
/// the platform rates rental offices, not vehicles. A renter comparing two
/// Corollas is really choosing between two offices.
class CatalogueGalleryLabel {
  const CatalogueGalleryLabel({
    required this.dealerId,
    required this.businessName,
    required this.cityId,
    required this.logoUrl,
    required this.averageRating,
    required this.reviewCount,
  });

  final String dealerId;
  final String businessName;
  final String? cityId;
  final String? logoUrl;

  /// Null means nobody has rated them. Not zero — zero is a real score on a
  /// one-to-five scale and would show an unrated office as the worst there is.
  final num? averageRating;
  final int reviewCount;

  static CatalogueGalleryLabel fromJson(Map<String, dynamic> json) =>
      CatalogueGalleryLabel(
        dealerId: json['dealerId'] as String? ?? '',
        businessName: json['businessName'] as String? ?? '',
        cityId: json['cityId'] as String?,
        logoUrl: _url(json['logoUrl']),
        averageRating: json['averageRating'] == null
            ? null
            : _num(json['averageRating']),
        reviewCount: _int(json['reviewCount']),
      );
}

class CatalogueListing {
  const CatalogueListing({
    required this.vehicleId,
    required this.make,
    required this.model,
    required this.year,
    required this.carType,
    required this.transmission,
    required this.fuelType,
    required this.seats,
    required this.coverImageUrl,
    required this.dailyRate,
    required this.isDeliveryAvailable,
    required this.gallery,
  });

  final String vehicleId;
  final String make;
  final String model;
  final int year;
  final CatalogueCarType? carType;
  final String transmission;
  final String fuelType;
  final int seats;

  /// Null is a real answer: publishing demands a photo, but removing one never
  /// re-checks that, so an active car can have none. The card renders a
  /// placeholder, never a broken image.
  final String? coverImageUrl;
  final Money dailyRate;
  final bool isDeliveryAvailable;
  final CatalogueGalleryLabel gallery;

  String get title => '$make $model';

  static CatalogueListing fromJson(Map<String, dynamic> json) => CatalogueListing(
        vehicleId: json['vehicleId'] as String? ?? '',
        make: json['make'] as String? ?? '',
        model: json['model'] as String? ?? '',
        year: _int(json['year']),
        carType: CatalogueCarType.maybe(json['carType']),
        transmission: json['transmission'] as String? ?? '',
        fuelType: json['fuelType'] as String? ?? '',
        seats: _int(json['seats']),
        coverImageUrl: _url(json['coverImageUrl']),
        dailyRate:
            Money.fromJson(json['dailyRate'] as Map<String, dynamic>? ?? const {}),
        isDeliveryAvailable: json['isDeliveryAvailable'] as bool? ?? false,
        gallery: CatalogueGalleryLabel.fromJson(
            json['gallery'] as Map<String, dynamic>? ?? const {}),
      );
}

class MileagePolicy {
  const MileagePolicy(this.isUnlimited, this.dailyLimitKm, this.excessFeePerKm);

  final bool isUnlimited;
  final int? dailyLimitKm;
  final Money? excessFeePerKm;

  static MileagePolicy fromJson(Map<String, dynamic> json) => MileagePolicy(
        json['isUnlimited'] as bool? ?? true,
        json['dailyLimitKm'] as int?,
        Money.maybe(json['excessFeePerKm']),
      );
}

class GalleryDaySchedule {
  const GalleryDaySchedule(this.day, this.isClosed, this.opens, this.closes);

  final String day;
  final bool isClosed;
  final String? opens;
  final String? closes;

  static GalleryDaySchedule fromJson(Map<String, dynamic> json) =>
      GalleryDaySchedule(
        json['day'] as String? ?? '',
        json['isClosed'] as bool? ?? false,
        json['opens'] as String?,
        json['closes'] as String?,
      );
}

/// What this gallery charges to bring the car to you, and how far it will come.
///
/// The fee is the GALLERY's own figure — there is no platform-wide delivery fee to
/// fall back on, so a client must not invent one. It is null exactly when delivery
/// is off.
class GalleryDelivery {
  const GalleryDelivery(this.isEnabled, this.radiusKm, this.fee);

  final bool isEnabled;
  final num radiusKm;
  final Money? fee;

  static GalleryDelivery fromJson(Map<String, dynamic> json) => GalleryDelivery(
        json['isEnabled'] as bool? ?? false,
        _num(json['radiusKm']),
        Money.maybe(json['fee']),
      );
}

/// The rental office as it appears BESIDE A CAR.
///
/// Carries nothing the office wrote for its own page: those sections include ones
/// it has HIDDEN, and this travels inside every car in the catalogue.
/// [PublicGalleryPage] is the page.
class PublicGallery {
  const PublicGallery({
    required this.dealerId,
    required this.businessName,
    required this.cityId,
    required this.latitude,
    required this.longitude,
    required this.logoUrl,
    required this.coverUrl,
    required this.operatingHours,
    required this.delivery,
    required this.averageRating,
    required this.reviewCount,
  });

  final String dealerId;
  final String businessName;
  final String? cityId;
  final double latitude;
  final double longitude;
  final String? logoUrl;
  final String? coverUrl;
  final List<GalleryDaySchedule> operatingHours;
  final GalleryDelivery delivery;
  final num? averageRating;
  final int reviewCount;

  static PublicGallery fromJson(Map<String, dynamic> json) => PublicGallery(
        dealerId: json['dealerId'] as String? ?? '',
        businessName: json['businessName'] as String? ?? '',
        cityId: json['cityId'] as String?,
        latitude: _num(json['latitude']).toDouble(),
        longitude: _num(json['longitude']).toDouble(),
        logoUrl: _url(json['logoUrl']),
        coverUrl: _url(json['coverUrl']),
        operatingHours: (json['operatingHours'] as List<dynamic>? ?? const [])
            .whereType<Map<String, dynamic>>()
            .map(GalleryDaySchedule.fromJson)
            .toList(),
        delivery: GalleryDelivery.fromJson(
            json['delivery'] as Map<String, dynamic>? ?? const {}),
        averageRating:
            json['averageRating'] == null ? null : _num(json['averageRating']),
        reviewCount: _int(json['reviewCount']),
      );
}

/// The rental office's OWN page: everything a car carries, plus where the office is
/// in words and what it writes for customers.
class PublicGalleryPage {
  const PublicGalleryPage({
    required this.dealerId,
    required this.businessName,
    required this.cityId,
    required this.address,
    required this.latitude,
    required this.longitude,
    required this.logoUrl,
    required this.coverUrl,
    required this.operatingHours,
    required this.delivery,
    required this.averageRating,
    required this.reviewCount,
    required this.sections,
  });

  final String dealerId;
  final String businessName;
  final String? cityId;
  final GalleryAddress? address;
  final double latitude;
  final double longitude;
  final String? logoUrl;
  final String? coverUrl;
  final List<GalleryDaySchedule> operatingHours;
  final GalleryDelivery delivery;
  final num? averageRating;
  final int reviewCount;
  final GallerySections sections;

  static PublicGalleryPage fromJson(Map<String, dynamic> json) => PublicGalleryPage(
        dealerId: json['dealerId'] as String? ?? '',
        businessName: json['businessName'] as String? ?? '',
        cityId: json['cityId'] as String?,
        address: GalleryAddress.maybe(json['address']),
        latitude: _num(json['latitude']).toDouble(),
        longitude: _num(json['longitude']).toDouble(),
        logoUrl: _url(json['logoUrl']),
        coverUrl: _url(json['coverUrl']),
        operatingHours: (json['operatingHours'] as List<dynamic>? ?? const [])
            .whereType<Map<String, dynamic>>()
            .map(GalleryDaySchedule.fromJson)
            .toList(),
        delivery: GalleryDelivery.fromJson(
            json['delivery'] as Map<String, dynamic>? ?? const {}),
        averageRating:
            json['averageRating'] == null ? null : _num(json['averageRating']),
        reviewCount: _int(json['reviewCount']),
        sections: GallerySections.fromJson(
            json['sections'] as Map<String, dynamic>? ?? const {}),
      );
}

/// Where the office is, in words. Null until the office records one.
class GalleryAddress {
  const GalleryAddress(this.area, this.street);

  final String area;
  final String? street;

  static GalleryAddress? maybe(dynamic json) => json is Map<String, dynamic>
      ? GalleryAddress(json['area'] as String? ?? '', json['street'] as String?)
      : null;
}

/// A piece of an office's own writing, and which language it turned out to be in.
///
/// **The language is not always the one this app asked for.** An office that has
/// written a section in Arabic and not in English is shown to an English reader in
/// Arabic, because what the office wrote beats an empty heading — that is the
/// server's rule, applied once, and this field is how it says which way it went.
///
/// **It is not the direction.** [UserText] lays every one of these out from the
/// characters themselves, which is both more reliable than a claim — an office may
/// type Arabic into the English box — and already in place. The language is carried
/// for the reading VOICE: a screen reader given an English paragraph inside an
/// Arabic app would otherwise pronounce it as Arabic.
class ResolvedText {
  const ResolvedText(this.text, this.language);

  final String text;

  /// `ar` or `en`, as the server named it. Never assumed from the app's own.
  final String language;

  /// Null for anything with nothing to read: absent, not a string, or blank. A
  /// section with nothing in it is not a section, and blank is the same as absent.
  static ResolvedText? maybe(dynamic json) {
    if (json is! Map<String, dynamic>) return null;
    final text = json['text'] as String?;
    if (text == null || text.trim().isEmpty) return null;
    // Falls back to English rather than to the app's current language: this is the
    // OFFICE's claim about what it wrote, and guessing it from the reader would
    // make an English paragraph claim to be Arabic on an Arabic phone.
    final language = json['language'] as String?;
    return ResolvedText(text, language == null || language.isEmpty ? 'en' : language);
  }
}

/// What the office wrote for its customers, as the server decided a customer sees it.
///
/// Null is "nothing to show" and says nothing about why: hidden, never written, and
/// — for delivery notes — an office that does not deliver all arrive the same way.
/// The app renders no heading for a null section and must never ask why it is null.
///
/// Each section carries the language it came back in; see [ResolvedText].
class GallerySections {
  const GallerySections({
    required this.about,
    required this.rentalConditions,
    required this.insurance,
    required this.pickupInstructions,
    required this.deliveryNotes,
    required this.customerNotes,
  });

  final ResolvedText? about;
  final ResolvedText? rentalConditions;
  final ResolvedText? insurance;
  final ResolvedText? pickupInstructions;
  final ResolvedText? deliveryNotes;
  final ResolvedText? customerNotes;

  static GallerySections fromJson(Map<String, dynamic> json) => GallerySections(
        about: ResolvedText.maybe(json['about']),
        rentalConditions: ResolvedText.maybe(json['rentalConditions']),
        insurance: ResolvedText.maybe(json['insurance']),
        pickupInstructions: ResolvedText.maybe(json['pickupInstructions']),
        deliveryNotes: ResolvedText.maybe(json['deliveryNotes']),
        customerNotes: ResolvedText.maybe(json['customerNotes']),
      );
}

class CatalogueVehicle {
  const CatalogueVehicle({
    required this.vehicleId,
    required this.make,
    required this.model,
    required this.year,
    required this.color,
    required this.description,
    required this.carType,
    required this.transmission,
    required this.fuelType,
    required this.seats,
    required this.dailyRate,
    required this.securityDeposit,
    required this.mileage,
    required this.fuelPolicy,
    required this.isDeliveryEligible,
    required this.imageUrls,
    required this.gallery,
    required this.isAvailable,
  });

  final String vehicleId;
  final String make;
  final String model;
  final int year;
  final String? color;
  /// What the office wrote about this car, in the language it came back in.
  final ResolvedText? description;
  final CatalogueCarType? carType;
  final String transmission;
  final String fuelType;
  final int seats;
  final Money dailyRate;
  final Money securityDeposit;
  final MileagePolicy mileage;
  final String fuelPolicy;
  final bool isDeliveryEligible;
  final List<String> imageUrls;
  final PublicGallery gallery;

  /// Null when the customer named no dates: "is it free" has no answer without a
  /// period to ask about, and false would be a lie.
  final bool? isAvailable;

  String get title => '$make $model';

  static CatalogueVehicle fromJson(Map<String, dynamic> json) => CatalogueVehicle(
        vehicleId: json['vehicleId'] as String? ?? '',
        make: json['make'] as String? ?? '',
        model: json['model'] as String? ?? '',
        year: _int(json['year']),
        color: json['color'] as String?,
        description: ResolvedText.maybe(json['description']),
        carType: CatalogueCarType.maybe(json['carType']),
        transmission: json['transmission'] as String? ?? '',
        fuelType: json['fuelType'] as String? ?? '',
        seats: _int(json['seats']),
        dailyRate:
            Money.fromJson(json['dailyRate'] as Map<String, dynamic>? ?? const {}),
        securityDeposit: Money.fromJson(
            json['securityDeposit'] as Map<String, dynamic>? ?? const {}),
        mileage: MileagePolicy.fromJson(
            json['mileage'] as Map<String, dynamic>? ?? const {}),
        fuelPolicy: json['fuelPolicy'] as String? ?? '',
        isDeliveryEligible: json['isDeliveryEligible'] as bool? ?? false,
        imageUrls: (json['imageUrls'] as List<dynamic>? ?? const [])
            .map((entry) => AppEnvironment.resolve('$entry'))
            .toList(),
        gallery: PublicGallery.fromJson(
            json['gallery'] as Map<String, dynamic>? ?? const {}),
        isAvailable: json['isAvailable'] as bool?,
      );
}

/// What a named rental would cost, priced by the SERVER.
///
/// The app displays these figures and never computes its own. The day count in
/// particular is the server's: rentals are billed in Amman calendar days, and a
/// phone subtracting two instants would get a different answer for the same
/// rental — and would contradict the invoice.
class RentalQuote {
  const RentalQuote({
    required this.vehicleId,
    required this.pickupAt,
    required this.returnAt,
    required this.timeZone,
    required this.pickupMethod,
    required this.pricing,
    required this.terms,
    required this.isAvailable,
  });

  final String vehicleId;
  final DateTime pickupAt;
  final DateTime returnAt;
  final String timeZone;
  final String pickupMethod;
  final BookingPricing pricing;
  final QuoteTerms terms;
  final bool isAvailable;

  static RentalQuote fromJson(Map<String, dynamic> json) => RentalQuote(
        vehicleId: json['vehicleId'] as String? ?? '',
        pickupAt: _requiredDateTime(json['pickupAt']),
        returnAt: _requiredDateTime(json['returnAt']),
        timeZone: json['timeZone'] as String? ?? 'Asia/Amman',
        pickupMethod: json['pickupMethod'] as String? ?? 'SelfPickup',
        pricing: BookingPricing.fromJson(
            json['pricing'] as Map<String, dynamic>? ?? const {}),
        terms:
            QuoteTerms.fromJson(json['terms'] as Map<String, dynamic>? ?? const {}),
        isAvailable: json['isAvailable'] as bool? ?? false,
      );
}

class QuoteTerms {
  const QuoteTerms({
    required this.depositPercent,
    required this.freeCancellationWindowHours,
    required this.paymentWindowHours,
    required this.customerCancellationPenaltyPercent,
    required this.noShowTimeoutHours,
    required this.answerWindowHours,
  });

  final num depositPercent;
  final num freeCancellationWindowHours;
  final num paymentWindowHours;
  final num customerCancellationPenaltyPercent;
  final num noShowTimeoutHours;

  /// How long the gallery has to answer, from the server rather than derived.
  final num answerWindowHours;

  static QuoteTerms fromJson(Map<String, dynamic> json) => QuoteTerms(
        depositPercent: _num(json['depositPercent']),
        freeCancellationWindowHours: _num(json['freeCancellationWindowHours']),
        paymentWindowHours: _num(json['paymentWindowHours']),
        customerCancellationPenaltyPercent:
            _num(json['customerCancellationPenaltyPercent']),
        noShowTimeoutHours: _num(json['noShowTimeoutHours']),
        answerWindowHours: _num(json['answerWindowHours']),
      );
}

// ── Bookings ───────────────────────────────────────────────────────────────────

/// The price this booking was FROZEN at.
///
/// `days` is the billed count and comes from the server. A screen must never
/// recompute it from the two instants: subtracting them answers "how long was it
/// out", which is not the rule the platform bills by, and would contradict the
/// invoice.
class BookingPricing {
  const BookingPricing({
    required this.dailyRate,
    required this.pickupDate,
    required this.returnDate,
    required this.days,
    required this.rentalTotal,
    required this.deliveryFee,
    required this.totalPrice,
    required this.depositPercent,
    required this.depositAmount,
    required this.balanceDue,
    required this.securityDeposit,
    required this.mileageUnlimited,
    required this.mileageDailyLimitKm,
    required this.mileageExcessFeePerKm,
    required this.fuelPolicy,
  });

  final Money dailyRate;

  /// The Amman calendar dates the rental was priced between, as `yyyy-MM-dd`.
  final String pickupDate;
  final String returnDate;
  final int days;
  final Money rentalTotal;
  final Money deliveryFee;
  final Money totalPrice;
  final num depositPercent;
  final Money depositAmount;

  /// The cash the driver collects at handover. The delivery fee is in here rather
  /// than in the deposit base, because the platform takes no commission on it.
  final Money balanceDue;
  final Money securityDeposit;
  final bool mileageUnlimited;
  final int? mileageDailyLimitKm;
  final Money? mileageExcessFeePerKm;
  final String fuelPolicy;

  static BookingPricing fromJson(Map<String, dynamic> json) => BookingPricing(
        dailyRate:
            Money.fromJson(json['dailyRate'] as Map<String, dynamic>? ?? const {}),
        pickupDate: json['pickupDate'] as String? ?? '',
        returnDate: json['returnDate'] as String? ?? '',
        days: _int(json['days'], 1),
        rentalTotal: Money.fromJson(
            json['rentalTotal'] as Map<String, dynamic>? ?? const {}),
        deliveryFee: Money.fromJson(
            json['deliveryFee'] as Map<String, dynamic>? ?? const {}),
        totalPrice: Money.fromJson(
            json['totalPrice'] as Map<String, dynamic>? ?? const {}),
        depositPercent: _num(json['depositPercent']),
        depositAmount: Money.fromJson(
            json['depositAmount'] as Map<String, dynamic>? ?? const {}),
        balanceDue:
            Money.fromJson(json['balanceDue'] as Map<String, dynamic>? ?? const {}),
        securityDeposit: Money.fromJson(
            json['securityDeposit'] as Map<String, dynamic>? ?? const {}),
        mileageUnlimited: json['mileageUnlimited'] as bool? ?? true,
        mileageDailyLimitKm: json['mileageDailyLimitKm'] as int?,
        mileageExcessFeePerKm: Money.maybe(json['mileageExcessFeePerKm']),
        fuelPolicy: json['fuelPolicy'] as String? ?? '',
      );
}

class BookingTerms {
  const BookingTerms({
    required this.depositPercent,
    required this.commissionPercent,
    required this.freeCancellationWindowHours,
    required this.noShowTimeoutHours,
    required this.paymentWindowHours,
    required this.answerWindowHours,
    required this.postReturnSettlementWindowHours,
    required this.customerCancellationPenaltyPercent,
    required this.rulesVersion,
  });

  final num depositPercent;
  final num commissionPercent;
  final num freeCancellationWindowHours;
  final num noShowTimeoutHours;
  final num paymentWindowHours;

  /// How long the gallery had to answer THIS request — the figure the booking
  /// froze, not today's setting and not `decisionDeadline − createdAt`.
  final num answerWindowHours;

  final num postReturnSettlementWindowHours;
  final num customerCancellationPenaltyPercent;
  final int rulesVersion;

  static BookingTerms fromJson(Map<String, dynamic> json) => BookingTerms(
        depositPercent: _num(json['depositPercent']),
        commissionPercent: _num(json['commissionPercent']),
        freeCancellationWindowHours: _num(json['freeCancellationWindowHours']),
        noShowTimeoutHours: _num(json['noShowTimeoutHours']),
        paymentWindowHours: _num(json['paymentWindowHours']),
        answerWindowHours: _num(json['answerWindowHours']),
        postReturnSettlementWindowHours:
            _num(json['postReturnSettlementWindowHours']),
        customerCancellationPenaltyPercent:
            _num(json['customerCancellationPenaltyPercent']),
        rulesVersion: _int(json['rulesVersion'], 1),
      );
}

/// What a penalty WOULD be. Assessed, never charged: with no dispute ticket,
/// nothing is applied at all, and money moves only when Khadra settles one.
class PenaltyAssessment {
  const PenaltyAssessment({
    required this.attributedTo,
    required this.minAmount,
    required this.maxAmount,
    required this.isRange,
    required this.isNothingOwed,
    required this.requiresTicketToEnforce,
    required this.reason,
  });

  final String attributedTo;
  final Money minAmount;
  final Money maxAmount;
  final bool isRange;
  final bool isNothingOwed;
  final bool requiresTicketToEnforce;
  final String reason;

  static PenaltyAssessment? maybe(dynamic json) => json is Map<String, dynamic>
      ? PenaltyAssessment(
          attributedTo: json['attributedTo'] as String? ?? '',
          minAmount:
              Money.fromJson(json['minAmount'] as Map<String, dynamic>? ?? const {}),
          maxAmount:
              Money.fromJson(json['maxAmount'] as Map<String, dynamic>? ?? const {}),
          isRange: json['isRange'] as bool? ?? false,
          isNothingOwed: json['isNothingOwed'] as bool? ?? true,
          requiresTicketToEnforce:
              json['requiresTicketToEnforce'] as bool? ?? true,
          reason: json['reason'] as String? ?? '',
        )
      : null;
}

/// Whether this booking can be cancelled right now, and what it would cost.
///
/// Both are the server's answers. The first needs its clock against two frozen
/// deadlines; the second is a percentage applied to money, and no screen on this
/// platform multiplies money.
class CancellationPreview {
  const CancellationPreview({
    required this.canCancel,
    required this.isFree,
    required this.penalty,
  });

  final bool canCancel;
  final bool isFree;
  final PenaltyAssessment? penalty;

  static CancellationPreview fromJson(Map<String, dynamic>? json) =>
      CancellationPreview(
        canCancel: json?['canCancel'] as bool? ?? false,
        isFree: json?['isFree'] as bool? ?? true,
        penalty: PenaltyAssessment.maybe(json?['penalty']),
      );
}

class VehicleLabel {
  const VehicleLabel({
    required this.vehicleId,
    required this.make,
    required this.model,
    required this.year,
    required this.color,
    required this.plateNumber,
    required this.coverImageUrl,
  });

  final String vehicleId;
  final String make;
  final String model;
  final int year;
  final String? color;
  final String plateNumber;
  final String? coverImageUrl;

  String get title => '$make $model';

  static VehicleLabel? maybe(dynamic json) => json is Map<String, dynamic>
      ? VehicleLabel(
          vehicleId: json['vehicleId'] as String? ?? '',
          make: json['make'] as String? ?? '',
          model: json['model'] as String? ?? '',
          year: _int(json['year']),
          color: json['color'] as String?,
          plateNumber: json['plateNumber'] as String? ?? '',
          coverImageUrl: _url(json['coverImageUrl']),
        )
      : null;
}

class BookingStatusChange {
  const BookingStatusChange({
    required this.fromStatus,
    required this.toStatus,
    required this.actorParty,
    required this.reasonCode,
    required this.reason,
    required this.occurredAt,
  });

  final String? fromStatus;
  final String toStatus;
  final String actorParty;

  /// The closed-set code. The label for it comes from `/app-config` in the
  /// reader's own language — the row itself holds no sentence.
  final String? reasonCode;

  /// Whatever the actor typed, in whatever language they typed it. Shown beside
  /// the label, never instead of it.
  final String? reason;
  final DateTime occurredAt;

  static BookingStatusChange fromJson(Map<String, dynamic> json) =>
      BookingStatusChange(
        fromStatus: json['fromStatus'] as String?,
        toStatus: json['toStatus'] as String? ?? '',
        actorParty: json['actorParty'] as String? ?? '',
        reasonCode: json['reasonCode'] as String?,
        reason: json['reason'] as String?,
        occurredAt: _requiredDateTime(json['occurredAt']),
      );
}

class Handover {
  const Handover({
    required this.type,
    required this.odometerKm,
    required this.fuelLevel,
    required this.notes,
    required this.cashCollected,
    required this.photoCount,
    required this.recordedAt,
  });

  final String type;
  final int? odometerKm;
  final num? fuelLevel;
  final String? notes;
  final Money? cashCollected;
  final int photoCount;
  final DateTime recordedAt;

  static Handover fromJson(Map<String, dynamic> json) => Handover(
        type: json['type'] as String? ?? '',
        odometerKm: json['odometerKm'] as int?,
        fuelLevel: json['fuelLevel'] == null ? null : _num(json['fuelLevel']),
        notes: json['notes'] as String?,
        cashCollected: Money.maybe(json['cashCollected']),
        photoCount: _int(json['photoCount']),
        recordedAt: _requiredDateTime(json['recordedAt']),
      );
}

/// One checkout attempt, as the customer's screen sees it.
///
/// Carries no provider reference and no provider prose: the reference is the key
/// that resolves a webhook to a payment, which a screen has no use for, and a
/// provider's own wording is written for an English-speaking developer.
class PaymentAttempt {
  const PaymentAttempt({
    required this.paymentId,
    required this.status,
    required this.amount,
    required this.checkoutUrl,
    required this.expiresAt,
    required this.failureCode,
    required this.isSandbox,
  });

  final String paymentId;
  final String status;
  final Money amount;
  final String? checkoutUrl;
  final DateTime expiresAt;
  final String? failureCode;

  /// Whether no money moved for THIS attempt and none ever could have.
  ///
  /// A property of the record rather than of the deployment, which is why it is
  /// here as well as on `AppConfig.payments`. That one says what this host is
  /// doing now; this says what was true when the attempt was opened, and the two
  /// can differ — which is the whole point of a marker that outlives a setting.
  final bool isSandbox;

  static PaymentAttempt? maybe(dynamic value) => value is Map<String, dynamic>
      ? PaymentAttempt(
          paymentId: value['paymentId'] as String? ?? '',
          status: value['status'] as String? ?? '',
          amount: Money.fromJson(value['amount'] as Map<String, dynamic>? ?? const {}),
          checkoutUrl: value['checkoutUrl'] as String?,
          expiresAt: _requiredDateTime(value['expiresAt']),
          failureCode: value['failureCode'] as String?,
          isSandbox: value['isSandbox'] as bool? ?? false,
        )
      : null;
}

/// The code a customer shows at the counter to prove the booking is theirs.
///
/// Shown once and held nowhere else: the platform keeps only a keyed hash of it,
/// and this object lives only as long as the screen that shows it.
class HandoverCodeGrant {
  const HandoverCodeGrant({
    required this.type,
    required this.code,
    required this.qrPayload,
    required this.expiresAt,
  });

  /// "Pickup" or "Return".
  final String type;

  /// Six digits.
  final String code;

  /// The same code for a scanner, with the booking reference.
  final String qrPayload;

  final DateTime expiresAt;

  bool get isReturn => type == 'Return';

  // Never print a live credential.
  @override
  String toString() => 'HandoverCodeGrant($type, expires $expiresAt)';

  static HandoverCodeGrant fromJson(Map<String, dynamic> json) => HandoverCodeGrant(
        type: json['type'] as String? ?? '',
        code: json['code'] as String? ?? '',
        qrPayload: json['qrPayload'] as String? ?? '',
        expiresAt: _requiredDateTime(json['expiresAt']),
      );
}

/// Whether the deposit can be paid right now, and what is in the way if not.
///
/// Every field is the SERVER's judgement. The app cannot work this out: the
/// answer depends on whether the platform has a payment provider at all, which
/// is not a property of any booking and never will be. `unavailableReason` is a
/// platform error code, rendered through the same table as a refused request.
class PaymentAvailability {
  const PaymentAvailability({
    required this.canPay,
    required this.unavailableReason,
    required this.amountDue,
    required this.payBy,
    required this.liveAttempt,
  });

  final bool canPay;
  final String? unavailableReason;
  final Money? amountDue;
  final DateTime? payBy;
  final PaymentAttempt? liveAttempt;

  /// Whether the platform itself cannot take cards, as opposed to this booking
  /// no longer being payable. Two different facts with two different remedies.
  bool get providerUnavailable =>
      unavailableReason == 'payments.provider_unavailable';

  static PaymentAvailability? maybe(dynamic value) => value is Map<String, dynamic>
      ? PaymentAvailability(
          canPay: value['canPay'] as bool? ?? false,
          unavailableReason: value['unavailableReason'] as String?,
          amountDue: Money.maybe(value['amountDue']),
          payBy: _dateTime(value['payBy']),
          liveAttempt: PaymentAttempt.maybe(value['liveAttempt']),
        )
      : null;
}

class Booking implements HasDealerLabel {
  const Booking({
    required this.bookingId,
    required this.reference,
    required this.status,
    required this.isTerminal,
    required this.dealerId,
    required this.vehicleId,
    required this.periodStart,
    required this.periodEnd,
    required this.pickupMethod,
    required this.deliveryLocation,
    required this.pricing,
    required this.terms,
    required this.penalty,
    required this.cancelledBy,
    required this.cancellationReasonCode,
    required this.cancellationReason,
    required this.createdAt,
    required this.decisionDeadline,
    required this.paymentDeadline,
    required this.depositPaid,
    required this.approvedAt,
    required this.freeCancellationDeadline,
    required this.pickedUpAt,
    required this.returnedAt,
    required this.finishedAt,
    required this.canBeDisputed,
    required this.isAwaitingDecision,
    required this.isAwaitingPayment,
    required this.cancellation,
    required this.liveDisputeId,
    required this.payment,
    required this.canReportNonDelivery,
    required this.nonDeliveryReportableFrom,
    required this.canBeReviewed,
    required this.myReviewId,
    required this.vehicle,
    required this.dealerName,
    required this.dealerRemoved,
    required this.dealerCityId,
    required this.handovers,
    required this.history,
  });

  final String bookingId;
  final String reference;
  final String status;
  final bool isTerminal;
  final String dealerId;
  final String vehicleId;
  final DateTime periodStart;
  final DateTime periodEnd;
  final String pickupMethod;
  final GeoPoint? deliveryLocation;
  final BookingPricing pricing;
  final BookingTerms terms;
  final PenaltyAssessment? penalty;
  final String? cancelledBy;
  final String? cancellationReasonCode;
  final String? cancellationReason;
  final DateTime createdAt;
  final DateTime decisionDeadline;
  final DateTime? paymentDeadline;
  final bool depositPaid;
  final DateTime? approvedAt;
  final DateTime? freeCancellationDeadline;
  final DateTime? pickedUpAt;
  final DateTime? returnedAt;
  final DateTime? finishedAt;
  final bool canBeDisputed;

  /// The SERVER's verdicts on liveness. Status alone does not say: a request past
  /// its decision deadline is over — the car went back on the market at that
  /// instant — but the row still reads `Requested` until the settlement pass
  /// reaches it. The app must not work this out from a deadline and its own clock,
  /// which on a phone with a wrong time would show a dead booking as live.
  final bool isAwaitingDecision;
  final bool isAwaitingPayment;
  final CancellationPreview cancellation;
  final String? liveDisputeId;

  /// The server's verdict on paying this booking's deposit. Null on a booking
  /// read by anyone but its own customer -- a gallery has no Pay button, and is
  /// not told whether the customer has a checkout open.
  final PaymentAvailability? payment;

  /// Whether the gallery can be reported for never handing the car over, and the
  /// instant that becomes true. Both come from the server: the grace is FROZEN on
  /// each booking, so the app cannot add it to `periodStart` itself and be right
  /// for a booking made before the owner last moved the number.
  final bool canReportNonDelivery;
  final DateTime nonDeliveryReportableFrom;
  final bool canBeReviewed;
  final String? myReviewId;
  final VehicleLabel? vehicle;

  /// The office's name — an English STAND-IN when [dealerRemoved] is true, which
  /// is why no screen prints this directly. `BookingPresentation.dealerName`
  /// words the removed case in the reader's own language.
  @override
  final String dealerName;

  @override
  final bool dealerRemoved;

  /// The office's city, by lookup id. Named through `cityNameProvider`, which
  /// answers null for a city that has not loaded or has been retired.
  final String? dealerCityId;

  final List<Handover> handovers;
  final List<BookingStatusChange> history;

  bool get isDelivery => pickupMethod == 'Delivery';

  static Booking fromJson(Map<String, dynamic> json) => Booking(
        bookingId: json['bookingId'] as String? ?? '',
        reference: json['reference'] as String? ?? '',
        status: json['status'] as String? ?? '',
        isTerminal: json['isTerminal'] as bool? ?? false,
        dealerId: json['dealerId'] as String? ?? '',
        vehicleId: json['vehicleId'] as String? ?? '',
        periodStart: _requiredDateTime(json['periodStart']),
        periodEnd: _requiredDateTime(json['periodEnd']),
        pickupMethod: json['pickupMethod'] as String? ?? 'SelfPickup',
        deliveryLocation: GeoPoint.maybe(json['deliveryLocation']),
        pricing: BookingPricing.fromJson(
            json['pricing'] as Map<String, dynamic>? ?? const {}),
        terms: BookingTerms.fromJson(
            json['terms'] as Map<String, dynamic>? ?? const {}),
        penalty: PenaltyAssessment.maybe(json['penalty']),
        cancelledBy: json['cancelledBy'] as String?,
        cancellationReasonCode: json['cancellationReasonCode'] as String?,
        cancellationReason: json['cancellationReason'] as String?,
        createdAt: _requiredDateTime(json['createdAt']),
        decisionDeadline: _requiredDateTime(json['decisionDeadline']),
        paymentDeadline: _dateTime(json['paymentDeadline']),
        depositPaid: json['depositPaid'] as bool? ?? false,
        approvedAt: _dateTime(json['approvedAt']),
        freeCancellationDeadline: _dateTime(json['freeCancellationDeadline']),
        pickedUpAt: _dateTime(json['pickedUpAt']),
        returnedAt: _dateTime(json['returnedAt']),
        finishedAt: _dateTime(json['finishedAt']),
        canBeDisputed: json['canBeDisputed'] as bool? ?? false,
        isAwaitingDecision: json['isAwaitingDecision'] as bool? ?? false,
        isAwaitingPayment: json['isAwaitingPayment'] as bool? ?? false,
        cancellation: CancellationPreview.fromJson(
            json['cancellation'] as Map<String, dynamic>?),
        liveDisputeId: json['liveDisputeId'] as String?,
        payment: PaymentAvailability.maybe(json['payment']),
        canReportNonDelivery: json['canReportNonDelivery'] as bool? ?? false,
        nonDeliveryReportableFrom:
            _dateTime(json['nonDeliveryReportableFrom']) ??
                _requiredDateTime(json['periodStart']),
        canBeReviewed: json['canBeReviewed'] as bool? ?? false,
        myReviewId: json['myReviewId'] as String?,
        vehicle: VehicleLabel.maybe(json['vehicle']),
        dealerName: json['dealerName'] as String? ?? '',
        dealerRemoved: json['dealerRemoved'] as bool? ?? false,
        dealerCityId: json['dealerCityId'] as String?,
        handovers: (json['handovers'] as List<dynamic>? ?? const [])
            .whereType<Map<String, dynamic>>()
            .map(Handover.fromJson)
            .toList(),
        history: (json['history'] as List<dynamic>? ?? const [])
            .whereType<Map<String, dynamic>>()
            .map(BookingStatusChange.fromJson)
            .toList(),
      );
}

class BookingListItem implements HasDealerLabel {
  const BookingListItem({
    required this.bookingId,
    required this.reference,
    required this.status,
    required this.periodStart,
    required this.periodEnd,
    required this.days,
    required this.pickupMethod,
    required this.totalPrice,
    required this.currency,
    required this.createdAt,
    required this.vehicle,
    required this.dealerName,
    required this.dealerRemoved,
    required this.hasLiveDispute,
    required this.dealerId,
  });

  final String bookingId;
  final String reference;
  final String status;
  final DateTime periodStart;
  final DateTime periodEnd;

  /// The billed calendar days, frozen on the booking. Never recomputed here.
  final int days;
  final String pickupMethod;
  final num totalPrice;
  final String currency;
  final DateTime createdAt;
  final VehicleLabel? vehicle;
  @override
  final String dealerName;

  @override
  final bool dealerRemoved;
  final bool hasLiveDispute;
  final String dealerId;

  static BookingListItem fromJson(Map<String, dynamic> json) => BookingListItem(
        bookingId: json['bookingId'] as String? ?? '',
        reference: json['reference'] as String? ?? '',
        status: json['status'] as String? ?? '',
        periodStart: _requiredDateTime(json['periodStart']),
        periodEnd: _requiredDateTime(json['periodEnd']),
        days: _int(json['days'], 1),
        pickupMethod: json['pickupMethod'] as String? ?? 'SelfPickup',
        totalPrice: _num(json['totalPrice']),
        currency: json['currency'] as String? ?? 'JOD',
        createdAt: _requiredDateTime(json['createdAt']),
        vehicle: VehicleLabel.maybe(json['vehicle']),
        dealerName: json['dealerName'] as String? ?? '',
        dealerRemoved: json['dealerRemoved'] as bool? ?? false,
        hasLiveDispute: json['hasLiveDispute'] as bool? ?? false,
        dealerId: json['dealerId'] as String? ?? '',
      );
}

/// The one booking the landing surface shows, and WHY it was chosen.
///
/// The reason is a stable code, not a sentence: the wording is the app's, in the
/// reader's own language. The app does not decide which booking this is — a
/// deposit due within hours outranking a rental starting tomorrow is a judgement
/// the platform owns, and a screen sorting a list by pickup date would have shown
/// the rental and let the deposit expire unread.
class NextBooking {
  const NextBooking({required this.booking, required this.reason});

  final BookingListItem booking;
  final String reason;

  /// Null when the customer has nothing live, which is the ordinary answer. The
  /// screen renders nothing at all for it, never a placeholder card.
  static NextBooking? maybe(dynamic json) {
    if (json is! Map<String, dynamic>) return null;
    final booking = json['booking'];
    if (booking is! Map<String, dynamic>) return null;

    return NextBooking(
      booking: BookingListItem.fromJson(booking),
      reason: json['reason'] as String? ?? '',
    );
  }
}

/// Why one booking outranked the others, as the server names them.
abstract final class NextBookingReasons {
  static const awaitingPayment = 'AwaitingPayment';
  static const inProgress = 'InProgress';
  static const upcoming = 'Upcoming';
  static const awaitingDecision = 'AwaitingDecision';
}

// ── Disputes ───────────────────────────────────────────────────────────────────

class EvidenceLink {
  const EvidenceLink(this.fileName, this.url, this.expiresAt);

  final String fileName;

  /// Freshly signed per request and never stored: a signed URL is a credential.
  final String url;
  final DateTime? expiresAt;

  static EvidenceLink fromJson(Map<String, dynamic> json) => EvidenceLink(
        json['fileName'] as String? ?? '',
        AppEnvironment.resolve(json['url'] as String? ?? ''),
        _dateTime(json['expiresAt']),
      );
}

class DisputeStatement {
  const DisputeStatement({
    required this.statementId,
    required this.party,
    required this.authorName,
    required this.body,
    required this.evidence,
    required this.createdAt,
  });

  final String statementId;
  final String party;
  final String authorName;
  final String body;
  final List<EvidenceLink> evidence;
  final DateTime createdAt;

  static DisputeStatement fromJson(Map<String, dynamic> json) => DisputeStatement(
        statementId: json['statementId'] as String? ?? '',
        party: json['party'] as String? ?? '',
        authorName: json['authorName'] as String? ?? '',
        body: json['body'] as String? ?? '',
        evidence: (json['evidence'] as List<dynamic>? ?? const [])
            .whereType<Map<String, dynamic>>()
            .map(EvidenceLink.fromJson)
            .toList(),
        createdAt: _requiredDateTime(json['createdAt']),
      );
}

/// Khadra's decision, as money. RECORDED, not executed: nothing moves funds until
/// the Payments context ships, and the app says so wherever it shows one.
class DisputeResolution {
  const DisputeResolution({
    required this.depositHeld,
    required this.refundToCustomer,
    required this.retainedByPlatform,
    required this.transferredToDealer,
    required this.dealerCharge,
    required this.waivesEverything,
    required this.note,
    required this.resolvedAt,
  });

  final Money depositHeld;
  final Money refundToCustomer;
  final Money retainedByPlatform;
  final Money transferredToDealer;
  final Money? dealerCharge;
  final bool waivesEverything;
  final String note;
  final DateTime resolvedAt;

  static DisputeResolution? maybe(dynamic json) => json is Map<String, dynamic>
      ? DisputeResolution(
          depositHeld: Money.fromJson(
              json['depositHeld'] as Map<String, dynamic>? ?? const {}),
          refundToCustomer: Money.fromJson(
              json['refundToCustomer'] as Map<String, dynamic>? ?? const {}),
          retainedByPlatform: Money.fromJson(
              json['retainedByPlatform'] as Map<String, dynamic>? ?? const {}),
          transferredToDealer: Money.fromJson(
              json['transferredToDealer'] as Map<String, dynamic>? ?? const {}),
          dealerCharge: Money.maybe(json['dealerCharge']),
          waivesEverything: json['waivesEverything'] as bool? ?? false,
          note: json['note'] as String? ?? '',
          resolvedAt: _requiredDateTime(json['resolvedAt']),
        )
      : null;
}

class Dispute {
  const Dispute({
    required this.ticketId,
    required this.bookingId,
    required this.status,
    required this.isLive,
    required this.openedByParty,
    required this.reason,
    required this.openedAt,
    required this.slaDeadline,
    required this.isOverdue,
    required this.closedAt,
    required this.statements,
    required this.resolution,
    required this.depositHeld,
    required this.booking,
  });

  final String ticketId;
  final String bookingId;
  final String status;
  final bool isLive;
  final String openedByParty;
  final String reason;
  final DateTime openedAt;
  final DateTime slaDeadline;
  final bool isOverdue;
  final DateTime? closedAt;
  final List<DisputeStatement> statements;
  final DisputeResolution? resolution;

  /// The deposit this ticket could split. Zero on a booking cancelled before the
  /// deposit ever cleared, which is every booking on the platform today.
  final Money depositHeld;
  final Booking? booking;

  bool get openedByMe => openedByParty == 'Customer';

  static Dispute fromJson(Map<String, dynamic> json) => Dispute(
        ticketId: json['ticketId'] as String? ?? '',
        bookingId: json['bookingId'] as String? ?? '',
        status: json['status'] as String? ?? '',
        isLive: json['isLive'] as bool? ?? false,
        openedByParty: json['openedByParty'] as String? ?? '',
        reason: json['reason'] as String? ?? '',
        openedAt: _requiredDateTime(json['openedAt']),
        slaDeadline: _requiredDateTime(json['slaDeadline']),
        isOverdue: json['isOverdue'] as bool? ?? false,
        closedAt: _dateTime(json['closedAt']),
        statements: (json['statements'] as List<dynamic>? ?? const [])
            .whereType<Map<String, dynamic>>()
            .map(DisputeStatement.fromJson)
            .toList(),
        resolution: DisputeResolution.maybe(json['resolution']),
        depositHeld: Money.fromJson(
            json['depositHeld'] as Map<String, dynamic>? ?? const {}),
        booking: json['booking'] is Map<String, dynamic>
            ? Booking.fromJson(json['booking'] as Map<String, dynamic>)
            : null,
      );
}

/// Step one of attaching evidence: where to PUT the bytes, and the key to name
/// afterwards when opening the ticket or adding a statement.
class EvidenceUpload {
  const EvidenceUpload(this.uploadUrl, this.storageKey);

  final String uploadUrl;
  final String storageKey;

  static EvidenceUpload fromJson(Map<String, dynamic> json) => EvidenceUpload(
        AppEnvironment.resolve(json['uploadUrl'] as String? ?? ''),
        json['storageKey'] as String? ?? '',
      );
}

// ── Notifications ──────────────────────────────────────────────────────────────

class NotificationItem {
  const NotificationItem({
    required this.notificationId,
    required this.kind,
    required this.subjectId,
    required this.subjectReference,
    required this.actorName,
    required this.occurredAt,
    required this.readAt,
  });

  final String notificationId;
  final String kind;
  final String? subjectId;
  final String? subjectReference;
  final String actorName;
  final DateTime occurredAt;
  final DateTime? readAt;

  bool get isRead => readAt != null;

  static NotificationItem fromJson(Map<String, dynamic> json) => NotificationItem(
        notificationId: json['notificationId'] as String? ?? '',
        kind: json['kind'] as String? ?? '',
        subjectId: json['subjectId'] as String?,
        subjectReference: json['subjectReference'] as String?,
        actorName: json['actorName'] as String? ?? '',
        occurredAt: _requiredDateTime(json['occurredAt']),
        readAt: _dateTime(json['readAt']),
      );
}

class NotificationFeed {
  const NotificationFeed({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.totalCount,
    required this.unreadCount,
  });

  final List<NotificationItem> items;
  final int page;
  final int pageSize;
  final int totalCount;
  final int unreadCount;

  bool get hasNext => page * pageSize < totalCount;

  static NotificationFeed fromJson(Map<String, dynamic> json) => NotificationFeed(
        items: (json['items'] as List<dynamic>? ?? const [])
            .whereType<Map<String, dynamic>>()
            .map(NotificationItem.fromJson)
            .toList(),
        page: _int(json['page'], 1),
        pageSize: _int(json['pageSize'], 25),
        totalCount: _int(json['totalCount']),
        unreadCount: _int(json['unreadCount']),
      );

  static NotificationFeed get empty => const NotificationFeed(
        items: [],
        page: 1,
        pageSize: 25,
        totalCount: 0,
        unreadCount: 0,
      );
}

// ── Shortlist ──────────────────────────────────────────────────────────────────

/// One car the customer saved.
///
/// [listing] is null when the car is no longer one they can see — hidden, in
/// maintenance, its gallery suspended, withdrawn. The server returns no name and
/// no reason for those, deliberately: naming the reason would distinguish cases
/// the catalogue answers identically on purpose. The screen says "no longer
/// listed" and offers to remove it.
/// Enough to recognise a saved car that can no longer be booked.
///
/// Four fields, and the server sends no fifth: no reason, no status, no image and
/// no gallery id. A hidden car, one in maintenance, a suspended gallery's and a
/// deleted one are all answered identically on this platform, and a reason here
/// would be the one place a customer could tell them apart.
class SavedVehicleIdentity {
  const SavedVehicleIdentity({
    required this.make,
    required this.model,
    required this.year,
    required this.galleryName,
  });

  final String make;
  final String model;
  final int year;
  final String galleryName;

  String get title => '$make $model';

  static SavedVehicleIdentity fromJson(Map<String, dynamic> json) =>
      SavedVehicleIdentity(
        make: json['make'] as String? ?? '',
        model: json['model'] as String? ?? '',
        year: _int(json['year']),
        galleryName: json['galleryName'] as String? ?? '',
      );
}

class SavedVehicle {
  const SavedVehicle({
    required this.vehicleId,
    required this.savedAt,
    required this.identity,
    required this.listing,
  });

  final String vehicleId;
  final DateTime savedAt;

  /// What the car is called, sent for every entry whose car still exists at all.
  final SavedVehicleIdentity? identity;

  /// The live listing — present only while the car can actually be booked.
  final CatalogueListing? listing;

  bool get isStillListed => listing != null;

  static SavedVehicle fromJson(Map<String, dynamic> json) => SavedVehicle(
        vehicleId: json['vehicleId'] as String? ?? '',
        savedAt: _requiredDateTime(json['savedAt']),
        identity: json['identity'] is Map<String, dynamic>
            ? SavedVehicleIdentity.fromJson(
                json['identity'] as Map<String, dynamic>)
            : null,
        listing: json['listing'] is Map<String, dynamic>
            ? CatalogueListing.fromJson(json['listing'] as Map<String, dynamic>)
            : null,
      );
}

// ── Reputation ─────────────────────────────────────────────────────────────────

/// What the platform tells a GALLERY about this customer — shown to the customer.
///
/// A semi-private score somebody cannot see is what privacy law objects to, and
/// it is the only way a customer learns of a wrong no-show while the window to
/// dispute it is still open.
///
/// Every figure is the server's, and the app derives nothing from them: no grade,
/// no colour band, no "trust level". `hasHistory` in particular is the server's
/// own answer, so "no history yet" and "a clean record" cannot be confused by a
/// screen adding up zeros.
///
/// `completedRentalsWithThisDealer` is deliberately ABSENT. It is always zero in
/// the self view — there is no gallery asking — and "0 rentals with this office"
/// on a customer's own screen is nonsense rather than a fact.
class CustomerReputation {
  const CustomerReputation({
    required this.averageRating,
    required this.ratingCount,
    required this.completedRentals,
    required this.noShows,
    required this.lateCancellations,
    required this.disputesResolvedAgainstCustomer,
    required this.customerSince,
    required this.hasHistory,
  });

  /// Null when nobody has rated this customer. Never zero: zero is a real score
  /// on a one-to-five scale and would read as the worst on the platform.
  final num? averageRating;
  final int ratingCount;

  final int completedRentals;
  final int noShows;
  final int lateCancellations;
  final int disputesResolvedAgainstCustomer;
  final DateTime customerSince;
  final bool hasHistory;

  /// Whether anything here is worth explaining rather than just reporting.
  bool get hasMarks =>
      noShows > 0 || lateCancellations > 0 || disputesResolvedAgainstCustomer > 0;

  static CustomerReputation fromJson(Map<String, dynamic> json) {
    final rating = json['dealerRating'] as Map<String, dynamic>? ?? const {};
    return CustomerReputation(
      averageRating: rating['average'] as num?,
      ratingCount: _int(rating['count']),
      completedRentals: _int(json['completedRentals']),
      noShows: _int(json['noShows']),
      lateCancellations: _int(json['lateCancellations']),
      disputesResolvedAgainstCustomer:
          _int(json['disputesResolvedAgainstCustomer']),
      customerSince: _requiredDateTime(json['customerSince']),
      // The SERVER's verdict, not a sum of the fields above. Recomputing it here
      // would be a screen deciding what counts as a history.
      hasHistory: json['hasHistory'] as bool? ?? false,
    );
  }
}

// ── Reviews ────────────────────────────────────────────────────────────────────

class GalleryReview {
  const GalleryReview({
    required this.reviewId,
    required this.rating,
    required this.comment,
    required this.isHidden,
    required this.createdAt,
  });

  final String reviewId;
  final int rating;

  /// Null on a hidden review. The RATING still counts towards the average —
  /// moderation removes abusive text, never the score.
  final String? comment;
  final bool isHidden;
  final DateTime createdAt;

  static GalleryReview fromJson(Map<String, dynamic> json) => GalleryReview(
        reviewId: json['reviewId'] as String? ?? '',
        rating: _int(json['rating'], 5),
        comment: json['comment'] as String?,
        isHidden: json['isHidden'] as bool? ?? false,
        createdAt: _requiredDateTime(json['createdAt']),
      );
}

class MyReview {
  const MyReview({
    required this.reviewId,
    required this.bookingId,
    required this.rating,
    required this.comment,
    required this.isHidden,
    required this.createdAt,
  });

  final String reviewId;
  final String bookingId;
  final int rating;
  final String? comment;
  final bool isHidden;
  final DateTime createdAt;

  static MyReview? maybe(dynamic json) => json is Map<String, dynamic>
      ? MyReview(
          reviewId: json['reviewId'] as String? ?? '',
          bookingId: json['bookingId'] as String? ?? '',
          rating: _int(json['rating'], 5),
          comment: json['comment'] as String?,
          isHidden: json['isHidden'] as bool? ?? false,
          createdAt: _requiredDateTime(json['createdAt']),
        )
      : null;
}

/// Turns a relative image path from the API into one this build can actually load.
///
/// The API answers `/api/v1/vehicle-images/...` — a path, because it does not know
/// which host a client reached it on. Every image field goes through here.
String? _url(dynamic value) {
  if (value is! String || value.isEmpty) return null;
  return AppEnvironment.resolve(value);
}
