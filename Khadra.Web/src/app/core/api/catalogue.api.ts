import { Money, ResolvedText } from './common.api';

/**
 * The public catalogue: `GET /api/v1/vehicles`, `/vehicles/facets`, `/vehicles/{id}`,
 * `/vehicles/{id}/quote`, `/galleries/{id}`. Hand-written to `ICatalogueReader.cs` and
 * `CatalogueUseCases.cs`; the API is anonymous and rate limited, and marks every answer `no-store`.
 */

export interface CatalogueCarType {
  readonly carTypeId: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

/** The gallery behind a search row. The rating is the OFFICE's: the platform does not rate cars. */
export interface CatalogueGalleryLabel {
  readonly dealerId: string;
  readonly businessName: string;
  readonly cityId: string | null;
  readonly logoUrl: string | null;
  readonly averageRating: number | null;
  readonly reviewCount: number;
}

export interface CatalogueListing {
  readonly vehicleId: string;
  readonly make: string;
  readonly model: string;
  readonly year: number;
  readonly carType: CatalogueCarType | null;
  readonly transmission: string;
  readonly fuelType: string;
  readonly seats: number;
  /** Null is real: an active car can lose its last photo. Render a placeholder, never a broken image. */
  readonly coverImageUrl: string | null;
  readonly dailyRate: Money;
  /** Both halves true: the office delivers AND this car is eligible. */
  readonly isDeliveryAvailable: boolean;
  readonly gallery: CatalogueGalleryLabel;
}

/** What the bookable catalogue holds, for building its filters. Not narrowed by any filter. */
export interface CatalogueFacets {
  readonly seats: readonly number[];
  readonly carTypeIds: readonly string[];
  /** One entry per make, however offices capitalised it. */
  readonly makes: readonly string[];
  /** API names; labelled from the `/app-config` vocabulary. */
  readonly fuelTypes: readonly string[];
  /** Newest first. */
  readonly years: readonly number[];
}

export interface GalleryDaySchedule {
  readonly day: string;
  readonly isClosed: boolean;
  readonly opens: string | null;
  readonly closes: string | null;
}

/** Fee is the office's own figure, null exactly when delivery is off. There is no platform fee. */
export interface GalleryDelivery {
  readonly isEnabled: boolean;
  readonly radiusKm: number;
  readonly fee: Money | null;
}

/** An office as it appears beside a car. Carries none of the office's own writing. */
export interface PublicGallery {
  readonly dealerId: string;
  readonly businessName: string;
  readonly cityId: string | null;
  readonly latitude: number;
  readonly longitude: number;
  readonly logoUrl: string | null;
  readonly coverUrl: string | null;
  readonly operatingHours: readonly GalleryDaySchedule[];
  readonly delivery: GalleryDelivery;
  readonly averageRating: number | null;
  readonly reviewCount: number;
}

export interface MileagePolicy {
  readonly isUnlimited: boolean;
  readonly dailyLimitKm: number | null;
  readonly excessFeePerKm: Money | null;
}

export interface CatalogueVehicle {
  readonly vehicleId: string;
  readonly make: string;
  readonly model: string;
  readonly year: number;
  readonly color: string | null;
  readonly description: ResolvedText | null;
  readonly carType: CatalogueCarType | null;
  readonly transmission: string;
  readonly fuelType: string;
  readonly seats: number;
  readonly dailyRate: Money;
  readonly securityDeposit: Money;
  readonly mileage: MileagePolicy;
  readonly fuelPolicy: string;
  readonly isDeliveryEligible: boolean;
  /** Cover first. */
  readonly imageUrls: readonly string[];
  readonly gallery: PublicGallery;
  /** Null when no dates were given: "is it free" has no answer without a period. */
  readonly isAvailable: boolean | null;
}

/**
 * What the office wrote for customers. Null is "nothing to show" and deliberately does not say why:
 * hidden by the office, never written, and not applicable all read the same. Render only what is here.
 */
export interface GallerySections {
  readonly about: ResolvedText | null;
  readonly rentalConditions: ResolvedText | null;
  readonly insurance: ResolvedText | null;
  readonly pickupInstructions: ResolvedText | null;
  readonly deliveryNotes: ResolvedText | null;
  readonly customerNotes: ResolvedText | null;
}

export interface PublicGalleryPage extends PublicGallery {
  readonly address: { readonly area: string; readonly street: string | null } | null;
  readonly sections: GallerySections;
}

/** `BookingPricingDto`: the server's figures. A screen never recomputes any of them. */
export interface BookingPricing {
  readonly dailyRate: Money;
  readonly pickupDate: string;
  readonly returnDate: string;
  readonly days: number;
  readonly rentalTotal: Money;
  readonly deliveryFee: Money;
  readonly totalPrice: Money;
  readonly depositPercent: number;
  readonly depositAmount: Money;
  readonly balanceDue: Money;
  readonly securityDeposit: Money;
  readonly mileageUnlimited: boolean;
  readonly mileageDailyLimitKm: number | null;
  readonly mileageExcessFeePerKm: Money | null;
  readonly fuelPolicy: string;
}

export interface QuoteTerms {
  readonly depositPercent: number;
  readonly freeCancellationWindowHours: number;
  readonly paymentWindowHours: number;
  readonly customerCancellationPenaltyPercent: number;
  readonly noShowTimeoutHours: number;
  readonly answerWindowHours: number;
}

export interface RentalQuote {
  readonly vehicleId: string;
  readonly pickupAt: string;
  readonly returnAt: string;
  readonly timeZone: string;
  readonly pickupMethod: string;
  readonly pricing: BookingPricing;
  readonly terms: QuoteTerms;
  readonly isAvailable: boolean;
}

export interface GalleryReview {
  readonly reviewId: string;
  readonly rating: number;
  readonly comment: string | null;
  readonly isHidden: boolean;
  readonly createdAt: string;
}

/** `GET /api/v1/galleries`: one office as a directory card. None of the office's own writing. */
export interface PublicGalleryCard {
  readonly dealerId: string;
  readonly businessName: string;
  readonly cityId: string | null;
  readonly logoUrl: string | null;
  readonly coverUrl: string | null;
  readonly delivery: GalleryDelivery;
  readonly averageRating: number | null;
  readonly reviewCount: number;
  /** Counted with the search's own predicate: exactly the cars the office's page lists. */
  readonly listedVehicleCount: number;
}
