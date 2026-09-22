import { Money } from './fleet.api';
import { LocalizedText, ResolvedText } from './localized.api';

// Re-exported: these arrived here first and half the console imports them from this file.
export type { LocalizedText, ResolvedText };

/**
 * The dealer console's own endpoints (Khadra.Application/Dealers/Console, ManageEmployees,
 * UpdateProfile). Everything money-related here is at the rate FROZEN on each booking; the console
 * never recomputes a figure from a current setting.
 */

export interface DealerBookingCounts {
  readonly requested: number;
  readonly oldestRequestedAt: string | null;
  /** Approved and not paid for: a car held on nothing but a clock. */
  readonly awaitingDeposit: number;
  /** Approved AND paid for: the rentals actually going ahead. */
  readonly confirmed: number;
  readonly pickedUp: number;
  readonly overdueReturns: number;
}

export interface UpcomingHandover {
  readonly bookingId: string;
  readonly reference: string;
  readonly status: string;
  readonly when: string;
  readonly pickupMethod: 'SelfPickup' | 'Delivery';
  /** Make, model and year. Null when the car is no longer in this dealer's fleet. */
  readonly vehicleLabel: string | null;
  /** Null when the customer's account no longer resolves (closed). */
  readonly customerName: string | null;
  readonly isOverdue: boolean;
}

export interface FleetStatusCount {
  readonly status: string;
  readonly count: number;
}

export interface DealerActivityEntry {
  readonly bookingId: string;
  readonly reference: string;
  readonly toStatus: string;
  readonly fromStatus: string | null;
  /** Null when nobody signed the change: the rental office (or the system) acted. */
  readonly actorUserId: string | null;
  /**
   * Null in two cases, told apart by `actorUserId`: a null id means the rental office acted; an id
   * with a null name means that person's account no longer resolves (a former member of staff).
   */
  readonly actorName: string | null;
  readonly reason: string | null;
  readonly occurredAt: string;
}

export interface DealerDashboard {
  readonly businessName: string;
  readonly canTrade: boolean;
  readonly bookings: DealerBookingCounts;
  readonly availableVehicles: number;
  readonly publishedVehicles: number;
  readonly totalVehicles: number;
  readonly fleetStatus: readonly FleetStatusCount[];
  readonly upcomingPickups: readonly UpcomingHandover[];
  readonly upcomingReturns: readonly UpcomingHandover[];
  readonly upcomingWindowHours: number;
  /** Null for staff without the reports grant: the figure is withheld, not zero. */
  readonly revenueThisMonth: Money | null;
  readonly occupancyPercentLast30Days: number | null;
  readonly recentActivity: readonly DealerActivityEntry[];
}

export type ReportPeriod = 'daily' | 'weekly' | 'monthly';

export interface DealerReport {
  readonly period: ReportPeriod;
  readonly from: string;
  readonly to: string;
  readonly bookings: number;
  readonly revenue: Money;
  readonly commission: Money;
  /** Revenue minus commission, computed server-side; the console never does money arithmetic. */
  readonly netAfterCommission: Money;
  readonly inProgress: Money;
  readonly inProgressCount: number;
  readonly occupancyPercent: number;
  readonly occupancyByVehicle: readonly VehicleOccupancy[];
}

export interface VehicleOccupancy {
  readonly vehicleId: string;
  readonly label: string;
  readonly rentedDays: number;
  readonly occupancyPercent: number;
}

export interface Employee {
  readonly employeeId: string;
  readonly userId: string;
  readonly fullName: string;
  readonly email: string;
  readonly phone: string;
  readonly canViewReports: boolean;
  readonly isActive: boolean;
  readonly status: 'Invited' | 'Active' | 'Deactivated';
  readonly lastLoginAt: string | null;
  readonly createdAt: string;
  readonly deactivatedAt: string | null;
}

export interface InviteEmployeeRequest {
  readonly fullName: string;
  readonly email: string;
  readonly phone: string;
  readonly canViewReports: boolean;
}

export interface DayScheduleInput {
  readonly day: string;
  readonly isClosed: boolean;
  readonly opensAt: string | null;
  readonly closesAt: string | null;
}

export interface UpdateProfileRequest {
  readonly businessName: string;
  // No `description`. The API stopped accepting one when About moved to the customer page, and a
  // field here would have been sent, ignored, and reported as saved.
  readonly latitude: number;
  readonly longitude: number;
  readonly operatingHours: readonly DayScheduleInput[];
  /**
   * The location, stated on every save, named as the application form names it. Required and
   * nullable — never optional: `JSON.stringify` drops an `undefined` property, and the API refuses a
   * save that leaves one out, because a save that left them out used to erase the office's city and
   * address. `null` is an answer ("none"); absence is not.
   */
  readonly cityId: string | null;
  readonly addressArea: string | null;
  readonly addressStreet: string | null;
}

export interface BrandingUpload {
  readonly uploadUrl: string;
  readonly storageKey: string;
  readonly expiresAt: string;
}

/** Spec 4.4: the dealer's switch and radius beside the platform's fee (GET /dealers/me/delivery). */
export interface DeliverySettingsView {
  readonly isEnabled: boolean;
  readonly radiusKm: number;
  readonly maxRadiusKm: number;
  /** This gallery’s own price for a delivery. Null exactly when delivery is off. */
  readonly fee: Money | null;
  /** The ceiling the server enforces, so the form can refuse what the server would. */
  readonly maxFee: number;
  readonly currencyCode: string;
  /**
   * How many of this gallery's LISTED cars are not offered for delivery.
   *
   * A car takes its delivery flag from whether the gallery offered delivery when the car was SAVED,
   * so a gallery that lists its fleet first and turns delivery on afterwards advertises a service
   * none of its cars provides. This number is the only thing on any screen connecting those two
   * facts, and it is the server's count -- the page does not load a fleet list to work it out.
   */
  readonly publishedCarsNotOfferedForDelivery: number;
}

/** What the bulk action changed, so a screen says a number rather than "done". */
export interface FleetDeliveryResult {
  readonly updated: number;
}

/**
 * The customer page as its owner edits it: what is written, what is hidden, what MAY be hidden, and
 * what a customer would actually see.
 *
 * `sections` is the server's own vocabulary and the toggles are built from it, so a section the
 * platform adds appears in the editor without a console release — and the console cannot offer one
 * the platform does not have. `visible` is the server's answer to "what does a customer see", from
 * the same code the public page runs, so the preview cannot drift from the page; a console working
 * out "hidden or empty" for itself certainly would.
 */
export interface CustomerPageView extends CustomerPageText {
  /** Section names that are hidden. PascalCase, as they are stored and sent. */
  readonly hiddenSections: readonly string[];
  /** Every section that MAY be hidden. Nothing else on the page can be. */
  readonly sections: readonly string[];
  readonly maxTextLength: number;
  /** Delivery notes are not shown while delivery is off, and the editor says so. */
  readonly deliveryEnabled: boolean;
  readonly visible: CustomerPagePreview;
}

/** The six texts, by their wire names. */
export interface CustomerPageText {
  readonly about: LocalizedText;
  readonly rentalConditions: LocalizedText;
  readonly insurance: LocalizedText;
  readonly pickupInstructions: LocalizedText;
  readonly deliveryNotes: LocalizedText;
  readonly customerNotes: LocalizedText;
}

/** What a customer would see. Null is "nothing to show", with no reason given. */
export interface VisibleCustomerPage {
  readonly about: ResolvedText | null;
  readonly rentalConditions: ResolvedText | null;
  readonly insurance: ResolvedText | null;
  readonly pickupInstructions: ResolvedText | null;
  readonly deliveryNotes: ResolvedText | null;
  readonly customerNotes: ResolvedText | null;
}

/** Both audiences, always — see `customerPagePreview`. */
export interface CustomerPagePreview {
  readonly ar: VisibleCustomerPage;
  readonly en: VisibleCustomerPage;
}

/**
 * A full replacement, never a patch.
 *
 * A section left out is a section cleared. That is the server's rule and it is the right one: a patch
 * would let the console leave text on a customer's screen that its owner can no longer see.
 */
export interface UpdateCustomerPageRequest extends CustomerPageText {
  readonly hiddenSections: readonly string[];
}
