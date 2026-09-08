import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/api/dtos.dart';

/// The DTOs are mirrors, so the tests are about the places where a WRONG default
/// would be a lie rather than a blank.
void main() {
  group('a gallery rating', () {
    test('is null when nobody has rated, never zero', () {
      // Zero is a real score on a one-to-five scale. Defaulting to it would show
      // an unrated office as the worst on the platform.
      final gallery = CatalogueGalleryLabel.fromJson(const {
        'dealerId': 'd1',
        'businessName': 'Petra Wheels',
        'averageRating': null,
        'reviewCount': 0,
      });

      expect(gallery.averageRating, isNull);
      expect(gallery.reviewCount, 0);
    });

    test('is read as a number when there is one', () {
      final gallery = CatalogueGalleryLabel.fromJson(const {
        'dealerId': 'd1',
        'businessName': 'Petra Wheels',
        'averageRating': 4.5,
        'reviewCount': 12,
      });

      expect(gallery.averageRating, 4.5);
      expect(gallery.reviewCount, 12);
    });
  });

  group('money', () {
    test("reads the server's own field name", () {
      // MoneyDto serialises as {amount, currency}. Reading anything else falls
      // through to a default, and a DEFAULT CURRENCY is the mistake this type
      // exists to prevent: the app would print JOD beside an amount in something
      // else and be right only until the day it was not.
      final money = Money.fromJson(const {'amount': 12.75, 'currency': 'JOD'});

      expect(money.amount, 12.75);
      expect(money.currencyCode, 'JOD');
    });

    test('an amount with no currency shows none rather than a guess', () {
      expect(Money.fromJson(const {'amount': 5}).currencyCode, '');
    });
  });

  group('vehicle availability', () {
    test('is null when the customer named no dates', () {
      // "Is it free" has no answer without a period to ask about, and false
      // would be a lie.
      final vehicle = CatalogueVehicle.fromJson(const {
        'vehicleId': 'v1',
        'make': 'Toyota',
        'model': 'Corolla',
        'isAvailable': null,
      });

      expect(vehicle.isAvailable, isNull);
    });

    test('is false when the server says the car is taken', () {
      final vehicle = CatalogueVehicle.fromJson(const {
        'vehicleId': 'v1',
        'make': 'Toyota',
        'model': 'Corolla',
        'isAvailable': false,
      });

      expect(vehicle.isAvailable, isFalse);
    });
  });

  group('booking', () {
    test('carries the server verdicts on liveness rather than deadlines alone', () {
      final booking = Booking.fromJson(const {
        'bookingId': 'b1',
        'reference': 'KH-ABCD1234',
        'status': 'Requested',
        'isAwaitingDecision': true,
        'isAwaitingPayment': false,
      });

      expect(booking.isAwaitingDecision, isTrue);
      expect(booking.isAwaitingPayment, isFalse);
    });

    test('defaults liveness to FALSE when a server omits it', () {
      // An old build talking to a newer server, or the reverse. False keeps a
      // dead booking from being shown as live, which is the safer direction.
      final booking = Booking.fromJson(const {
        'bookingId': 'b1',
        'status': 'Requested',
      });

      expect(booking.isAwaitingDecision, isFalse);
      expect(booking.isAwaitingPayment, isFalse);
    });

    test('reads the billed day count from the server, defaulting to one', () {
      final booking = Booking.fromJson(const {
        'bookingId': 'b1',
        'pricing': {'days': 3},
      });

      expect(booking.pricing.days, 3);
      expect(
        Booking.fromJson(const {'bookingId': 'b2'}).pricing.days,
        1,
        reason: 'A rental is never billed for zero days.',
      );
    });

    test('keeps the cancellation code and the typed words apart', () {
      final booking = Booking.fromJson(const {
        'bookingId': 'b1',
        'cancelledBy': 'Customer',
        'cancellationReasonCode': 'TravelCancelled',
        'cancellationReason': 'الرحلة أُلغيت',
      });

      expect(booking.cancellationReasonCode, 'TravelCancelled');
      expect(booking.cancellationReason, 'الرحلة أُلغيت');
    });

    test('a cancellation preview refuses by default', () {
      // No preview means no button. Defaulting canCancel to true would offer a
      // control the server would refuse.
      final booking = Booking.fromJson(const {'bookingId': 'b1'});

      expect(booking.cancellation.canCancel, isFalse);
    });
  });

  group('vocabularies', () {
    const entries = [
      VocabularyEntry('SelfPickup', 'Collect it yourself', 'الاستلام من المكتب'),
      VocabularyEntry('Delivery', 'Delivered to you', 'التوصيل إليك'),
    ];

    test('give the label in the reader language', () {
      expect(Vocabularies.label(entries, 'Delivery', false), 'Delivered to you');
      expect(Vocabularies.label(entries, 'Delivery', true), 'التوصيل إليك');
    });

    test('fall back to the member name for one the platform did not publish', () {
      // A member added to the platform after this build shipped shows as itself
      // rather than as a blank.
      expect(Vocabularies.label(entries, 'Helicopter', false), 'Helicopter');
    });
  });

  group('app config', () {
    test('keeps a null minimum age as null', () {
      // Null means the owner has set no age limit and nobody is refused on age --
      // a real shipping state. The registration form asks for a date of birth
      // exactly when this is non-null.
      final config = AppConfig.fromJson(const {'minimumRenterAge': null});
      expect(config.minimumRenterAge, isNull);
    });

    test('defaults the currency to three minor units', () {
      expect(AppConfig.fromJson(const {}).currency.minorUnits, 3);
    });
  });

  group('paged results', () {
    test('knows when there is another page', () {
      final page = Paged.fromJson<String>(
        const {'items': <dynamic>[], 'page': 1, 'pageSize': 20, 'totalCount': 45},
        (json) => json['x'] as String,
      );

      expect(page.totalPages, 3);
      expect(page.hasNext, isTrue);
    });

    test('a full last page is not another page', () {
      // The count is what tells a full page from a last one; a client that only
      // knew it received twenty rows could not.
      final page = Paged.fromJson<String>(
        const {'items': <dynamic>[], 'page': 2, 'pageSize': 20, 'totalCount': 40},
        (json) => json['x'] as String,
      );

      expect(page.hasNext, isFalse);
    });
  });
}
