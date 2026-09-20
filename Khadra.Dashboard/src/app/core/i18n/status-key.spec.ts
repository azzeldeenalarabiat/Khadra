import { describe, expect, it } from 'vitest';
import { enumKey, spellEnumName, statusKey } from './status-key';

/**
 * How a server status finds its words.
 *
 * The dealer's bookings list used to keep its own English map — Requested → "Pending",
 * PickedUp → "Active" — so those three words stayed English in Arabic. They are scoped dictionary
 * entries now, resolved most specific first.
 */
describe('statusKey', () => {
  it("uses the rental office's own word for its queue", () => {
    expect(statusKey('Requested', 'dealerBooking')).toBe('status.requestedDealerBooking');
    expect(statusKey('Approved', 'dealerBooking')).toBe('status.approvedDealerBooking');
    expect(statusKey('PickedUp', 'dealerBooking')).toBe('status.pickedUpDealerBooking');
  });

  it('falls through to the booking sense, then to the plain word', () => {
    expect(statusKey('Rejected', 'dealerBooking')).toBe('status.rejectedBooking');
    expect(statusKey('Returned', 'dealerBooking')).toBe('status.returned');
  });

  it("keeps a dealership's Approved apart from a booking's", () => {
    expect(statusKey('Approved')).toBe('status.approved');
    expect(statusKey('Approved', 'booking')).toBe('status.approvedBooking');
  });

  it('calls a fleet car that customers can find Published, and falls through for the rest', () => {
    expect(statusKey('Active', 'vehicle')).toBe('status.activeVehicle');
    expect(statusKey('Maintenance', 'vehicle')).toBe('status.maintenanceVehicle');
    expect(statusKey('Hidden', 'vehicle')).toBe('status.hidden');
    expect(statusKey('Active')).toBe('status.active');
  });

  it('has no key for a status this build has never heard of', () => {
    expect(statusKey('PartiallyRefunded', 'dealerBooking')).toBeNull();
  });
});

describe('spellEnumName', () => {
  it('spells an unknown status out rather than leaving a blank', () => {
    expect(spellEnumName('PartiallyRefunded')).toBe('Partially refunded');
    expect(spellEnumName('Open')).toBe('Open');
  });
});

/**
 * Enums that are not statuses have their own families. A dispute pill used to print the party's raw
 * name, so an Arabic ticket read "Dealer" and "Customer" in Latin script.
 */
describe('enumKey', () => {
  it('finds every booking party under its own family', () => {
    expect(enumKey('party', 'Customer')).toBe('party.customer');
    expect(enumKey('party', 'Dealer')).toBe('party.dealer');
    expect(enumKey('party', 'System')).toBe('party.system');
    expect(enumKey('party', 'Unattributed')).toBe('party.unattributed');
    expect(enumKey('party', 'Admin')).toBe('party.admin');
  });

  it('has no key for a member this build has never heard of', () => {
    expect(enumKey('party', 'Insurer')).toBeNull();
  });
});
