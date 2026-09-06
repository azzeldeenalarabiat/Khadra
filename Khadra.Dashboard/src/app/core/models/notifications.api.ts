/**
 * The Notifications context (`/api/v1/notifications`).
 *
 * A row carries a KIND and its parts — never a sentence. The server refuses to write English into a
 * table it never deletes from: wording changes, Arabic is planned, and a stored sentence would freeze
 * both. So the console composes the line from `kind`, `actorName` and `subjectReference`.
 *
 * `actorName` is the name the actor had at the moment they acted, snapshotted on the row, so a line
 * still reads correctly after that person is renamed or leaves.
 */
export type NotificationKind =
  | 'BookingApproved'
  | 'BookingRejected'
  | 'BookingPickedUp'
  | 'BookingReturned'
  | 'DealerApproved'
  | 'DealerRejected'
  | 'DealerClarificationRequested'
  | 'DealerSuspended'
  | 'DealerReactivated'
  | 'StaffReactivated'
  | 'ReportAccessGranted'
  | 'ReportAccessRevoked';

export interface NotificationItem {
  readonly notificationId: string;
  readonly kind: NotificationKind;
  /** The booking, dispute or dealership this is about. Null where the kind has no record. */
  readonly subjectId: string | null;
  /** A reference the platform issued, such as `KR-1042`. Never a person's name. */
  readonly subjectReference: string | null;
  readonly actorName: string;
  /** The server decides this: two colleagues can share a name, and the row knows which is which. */
  readonly isMine: boolean;
  readonly occurredAt: string;
  readonly readAt: string | null;
  readonly isRead: boolean;
}

export interface NotificationFeed {
  readonly items: readonly NotificationItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  /** Counted over everything unread, not over this page, so the badge is never a truncated count. */
  readonly unreadCount: number;
}
