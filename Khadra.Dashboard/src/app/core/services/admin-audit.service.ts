import { httpResource } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { AuditLogPage, AuditVocabulary } from '../models/audit.api';

/**
 * The audit log.
 *
 * Two resources because they answer two questions at two cadences: the vocabulary is what the log
 * CAN be filtered by and changes only when the platform is redeployed, while the entries change
 * every time an admin does anything. The vocabulary is fetched once for the screen; the list re-runs
 * whenever a filter moves.
 *
 * There is no write here, and there will not be one. Entries are recorded by the handler performing
 * the action, in that handler's own transaction; the table is append-only underneath.
 */
@Injectable({ providedIn: 'root' })
export class AdminAuditService {
  private readonly base = '/api/v1/admin/audit-logs';

  /** Filter state the list reacts to. Every one of them is optional and they compose. */
  readonly action = signal<string | null>(null);
  readonly entityType = signal<string | null>(null);
  readonly actorUserId = signal<string | null>(null);
  readonly from = signal<string | null>(null);
  readonly to = signal<string | null>(null);
  readonly search = signal<string>('');
  readonly page = signal<number>(1);

  readonly vocabulary = httpResource<AuditVocabulary>(() => `${this.base}/vocabulary`);

  readonly entries = httpResource<AuditLogPage>(() => {
    const params: Record<string, string | number> = { page: this.page(), pageSize: 25 };

    // Only what is actually set is sent. An empty string on the query string is a filter value, not
    // an absent filter, and the server would treat it as one.
    const action = this.action();
    if (action) params['action'] = action;
    const entityType = this.entityType();
    if (entityType) params['entityType'] = entityType;
    const actor = this.actorUserId();
    if (actor) params['actorUserId'] = actor;
    const from = this.from();
    if (from) params['from'] = from;
    const to = this.to();
    if (to) params['to'] = to;
    const search = this.search().trim();
    if (search) params['search'] = search;

    return { url: this.base, params };
  });

  /** Any filter change starts again at page one; page 4 of the old result means nothing. */
  reset(): void {
    this.action.set(null);
    this.entityType.set(null);
    this.actorUserId.set(null);
    this.from.set(null);
    this.to.set(null);
    this.search.set('');
    this.page.set(1);
  }

  reload(): void {
    this.entries.reload();
  }
}
