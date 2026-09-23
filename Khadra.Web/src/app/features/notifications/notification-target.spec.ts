import { describe, expect, it } from 'vitest';
import { EN } from '../../core/i18n/en';
import { notificationTarget } from './notification-target';

const ID = '3f2b8c1d-0e9f-4a7b-8c5d-9a7b6c5d4e3f';

/** Every customer kind in Khadra.Domain/Notifications/NotificationKind.cs. */
const CUSTOMER_KINDS = [
  'YourBookingApproved',
  'YourBookingRejected',
  'YourBookingExpired',
  'YourBookingCompleted',
  'YourBookingMarkedNoShow',
  'YourBookingConfirmed',
  'YourBookingCancelled',
  'YourDisputeUpdated',
  'YourPaymentReminder',
  'YourPickupReminder',
  'YourReturnReminder',
  'YourBookingPickedUp',
  'YourBookingReturned',
];

describe('notificationTarget', () => {
  it('opens the booking for every booking kind', () => {
    for (const kind of CUSTOMER_KINDS.filter((k) => k !== 'YourDisputeUpdated')) {
      expect(notificationTarget(kind, ID), kind).toEqual(['bookings', ID]);
    }
  });

  it('opens the dispute for a dispute update, whose subject is the ticket', () => {
    expect(notificationTarget('YourDisputeUpdated', ID)).toEqual(['disputes', ID]);
  });

  it('opens nothing without a subject', () => {
    expect(notificationTarget('YourBookingApproved', null)).toBeNull();
  });

  it('has words for every customer kind the platform sends', () => {
    for (const kind of CUSTOMER_KINDS) expect(EN, kind).toHaveProperty(`notification.${kind}`);
  });
});
