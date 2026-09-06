import { Money } from './fleet.api';

/**
 * The dealer console's own endpoints (Khadra.Application/Dealers/Console, ManageEmployees,
 * UpdateProfile). Everything money-related here is at the rate FROZEN on each booking; the console
 * never recomputes a figure from a current setting.
 */

export interface DealerBookingCounts {
  readonly requested: number;
  readonly oldestRequestedAt: string | null;
  readonly approved: number;
  readonly pickedUp: number;
  readonly overdueReturns: number;
}

export interface UpcomingHandover {
  readonly bookingId: string;
  readonly reference: string;
  readonly status: string;
  readonly when: string;
  readonly pickupMethod: 'SelfPickup' | 'Delivery';
  readonly vehicleLabel: string;
  readonly customerName: string;
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
  readonly actorUserId: string | null;
  readonly actorName: string;
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
  readonly description: string | null;
  readonly latitude: number;
  readonly longitude: number;
  readonly operatingHours: readonly DayScheduleInput[];
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
}
