// Field-for-field mirrors of the server's records. No logic lives here beyond
// reading JSON: every figure on these types was computed by the server and the app
// renders it unchanged.
//
// Money arrives as a JSON number and is kept as `num`. The app does no arithmetic
// on it at all, and displays it only through `Formats.money`, which pads to the
// minor units `/app-config` names -- three for the dinar, where every developer's
// instinct is two, and where guessing renders 12.75 against a contract that says
// 12.750.

import '../core/config/app_environment.dart';

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
    required this.paymentWindowHours,
    required this.documents,
    required this.vocabularies,
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
  final int paymentWindowHours;
  final DocumentLimits documents;
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
        paymentWindowHours: _int(json['paymentWindowHours'], 24),
        documents: DocumentLimits.fromJson(
            json['documents'] as Map<String, dynamic>? ?? const {}),
        vocabularies: Vocabularies.fromJson(
            json['vocabularies'] as Map<String, dynamic>? ?? const {}),
      );
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
  });

  final String familyId;
  final DateTime signedInAt;
  final DateTime lastUsedAt;
  final DateTime expiresAt;
  final String? createdByIp;
  final String? userAgent;
  final bool isActive;

  static SessionSummary fromJson(Map<String, dynamic> json) => SessionSummary(
        familyId: json['familyId'] as String? ?? '',
        signedInAt: _requiredDateTime(json['signedInAt']),
        lastUsedAt: _requiredDateTime(json['lastUsedAt']),
        expiresAt: _requiredDateTime(json['expiresAt']),
        createdByIp: json['createdByIp'] as String?,
        userAgent: json['userAgent'] as String?,
        isActive: json['isActive'] as bool? ?? true,
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
    required this.sizeBytes,
    required this.uploadedAt,
    required this.reviewNote,
  });

  final String documentId;
  final String type;
  final String status;
  final int sizeBytes;
  final DateTime uploadedAt;
  final String? reviewNote;

  static CustomerDocument fromJson(Map<String, dynamic> json) => CustomerDocument(
        documentId: json['documentId'] as String? ?? '',
        type: json['type'] as String? ?? '',
        status: json['status'] as String? ?? '',
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

class PublicGallery {
  const PublicGallery({
    required this.dealerId,
    required this.businessName,
    required this.description,
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
  final String? description;
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
        description: json['description'] as String?,
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
  final String? description;
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
        description: json['description'] as String?,
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
  });

  final num depositPercent;
  final num freeCancellationWindowHours;
  final num paymentWindowHours;
  final num customerCancellationPenaltyPercent;
  final num noShowTimeoutHours;

  static QuoteTerms fromJson(Map<String, dynamic> json) => QuoteTerms(
        depositPercent: _num(json['depositPercent']),
        freeCancellationWindowHours: _num(json['freeCancellationWindowHours']),
        paymentWindowHours: _num(json['paymentWindowHours']),
        customerCancellationPenaltyPercent:
            _num(json['customerCancellationPenaltyPercent']),
        noShowTimeoutHours: _num(json['noShowTimeoutHours']),
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
    required this.postReturnSettlementWindowHours,
    required this.customerCancellationPenaltyPercent,
    required this.rulesVersion,
  });

  final num depositPercent;
  final num commissionPercent;
  final num freeCancellationWindowHours;
  final num noShowTimeoutHours;
  final num paymentWindowHours;
  final num postReturnSettlementWindowHours;
  final num customerCancellationPenaltyPercent;
  final int rulesVersion;

  static BookingTerms fromJson(Map<String, dynamic> json) => BookingTerms(
        depositPercent: _num(json['depositPercent']),
        commissionPercent: _num(json['commissionPercent']),
        freeCancellationWindowHours: _num(json['freeCancellationWindowHours']),
        noShowTimeoutHours: _num(json['noShowTimeoutHours']),
        paymentWindowHours: _num(json['paymentWindowHours']),
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

class Booking {
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
    required this.canReportNonDelivery,
    required this.nonDeliveryReportableFrom,
    required this.canBeReviewed,
    required this.myReviewId,
    required this.vehicle,
    required this.dealerName,
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

  /// Whether the gallery can be reported for never handing the car over, and the
  /// instant that becomes true. Both come from the server: the grace is FROZEN on
  /// each booking, so the app cannot add it to `periodStart` itself and be right
  /// for a booking made before the owner last moved the number.
  final bool canReportNonDelivery;
  final DateTime nonDeliveryReportableFrom;
  final bool canBeReviewed;
  final String? myReviewId;
  final VehicleLabel? vehicle;
  final String dealerName;
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
        canReportNonDelivery: json['canReportNonDelivery'] as bool? ?? false,
        nonDeliveryReportableFrom:
            _dateTime(json['nonDeliveryReportableFrom']) ??
                _requiredDateTime(json['periodStart']),
        canBeReviewed: json['canBeReviewed'] as bool? ?? false,
        myReviewId: json['myReviewId'] as String?,
        vehicle: VehicleLabel.maybe(json['vehicle']),
        dealerName: json['dealerName'] as String? ?? '',
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

class BookingListItem {
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
  final String dealerName;
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
        hasLiveDispute: json['hasLiveDispute'] as bool? ?? false,
        dealerId: json['dealerId'] as String? ?? '',
      );
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
