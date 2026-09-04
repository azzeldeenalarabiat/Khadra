import { PagedResult } from './dealers.api';

/**
 * The wire shape of the audit-log endpoints.
 *
 * A faithful mirror of the DTOs. Nothing here is composed into a sentence or given an icon: the
 * server sends the parts, the console decides how to read them — the same split the activity feed
 * uses, for the same reason (English word order and an icon set are not API concerns).
 */

export interface AuditLogEntry {
  readonly id: string;
  readonly occurredAt: string;
  /**
   * Null when a background job acted rather than a person — an expiry sweep, a no-show timeout.
   * A real distinction an auditor must see, not a missing value to hide.
   */
  readonly actorUserId: string | null;
  readonly actorName: string;
  /** The role held AT THE TIME, snapshotted on the entry. Null for a system actor. */
  readonly actorRole: string | null;
  readonly action: string;
  readonly entityType: string;
  readonly entityId: string | null;
  /** What the action was taken on, as it read then: a dealer name, a booking reference. */
  readonly subjectLabel: string;
  readonly previousValue: string | null;
  readonly newValue: string | null;
  /** The written justification. The field that actually settles an argument. */
  readonly reason: string | null;
  /** Everything one request did shares this, so an incident can be followed across its entries. */
  readonly correlationId: string | null;
}

/**
 * Who appears in the log, and what can be filtered by.
 *
 * From the server, never a list typed into this console: eighteen action names hard-coded here would
 * go stale the moment one is added, and the screen would then offer a filter set that quietly
 * excludes real entries — the worst possible failure on the screen whose job is completeness.
 */
export interface AuditVocabulary {
  readonly actions: readonly string[];
  readonly entityTypes: readonly string[];
  readonly actors: readonly AuditActor[];
}

export interface AuditActor {
  /** Null for the System actor. */
  readonly userId: string | null;
  readonly name: string;
  readonly entryCount: number;
}

export type AuditLogPage = PagedResult<AuditLogEntry>;
