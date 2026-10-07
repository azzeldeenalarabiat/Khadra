import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LegalConfigDocument, MyLegalConsents } from '../models/legal.api';
import { SessionService } from './session.service';

/** The API's refusal while a legal text in force waits for this person's consent (Wave 4, W4-8). */
export const CONSENT_PENDING = 'legal.consent_pending';

/** A text was replaced between being shown and being accepted: up to a few minutes after a publish. */
export const VERSION_NOT_CURRENT = 'legal.version_not_current';

/** A sign-up that did not accept a text in force: the page never showed it, or its link did not say to ask. */
export const CONSENT_REQUIRED = 'legal.consent_required';

/** How an acceptance ended: recorded, refused because a newer text is now in force, or not answered. */
export type ConsentAnswer = 'accepted' | 'changed' | 'failed';

interface Known {
  /** Whose consents these are: the answer is about one person, never about whoever signs in next. */
  readonly userId: string;
  readonly pending: readonly LegalConfigDocument[];
}

/** True for the refusal the gate exists to answer, from any endpoint. */
export function isConsentPending(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 403 && error.error?.code === CONSENT_PENDING;
}

/**
 * Whether the signed-in person must accept a legal text before the console will answer them (Wave 4, W4-8; owner D6,
 * approved 2026-10-07 with administrators exempt).
 *
 * The SERVER is the gate: while a text in force waits for this person's consent, every request they make is refused
 * with 403 `legal.consent_pending`, except the few that resolve it. This is how the console says so once — one prompt
 * in place of the page — rather than as every screen failing on its own. It learns it two ways: asked after sign-in,
 * where the guard waits for the answer so the frame never draws a page it is about to cover, and told by any refusal,
 * which is how a text published while the console was open arrives.
 *
 * Every root resource that would only be refused reads {@link open} and stays idle until it is true, which is what
 * pauses the pollers — and keeps them quiet in the moment between signing in and the answer, when a service left
 * standing by an earlier session in the same tab would otherwise fire at once and be refused. The moment the person
 * accepts, the same signal sends each of them back to the server, so nothing has to remember to reload.
 */
@Injectable({ providedIn: 'root' })
export class ConsentGateService {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);

  private readonly known = signal<Known | null>(null);
  private asking: Promise<void> | null = null;

  /** The texts in force still to accept, with their pages; empty when nothing is owed, or nothing is known yet. */
  readonly pending = computed<readonly LegalConfigDocument[]>(() => {
    const known = this.known();
    const user = this.session.user();
    return known && user && known.userId === user.id ? known.pending : [];
  });

  /** While true the console shows the prompt in place of the page. */
  readonly blocked = computed(() => this.pending().length > 0);

  /**
   * Whether requests may go on this person's behalf: an administrator's at once, anybody else's once their consents
   * are known and nothing is pending. A question that could not be answered counts as known — the server's gate still
   * stands, and its refusal would ask again — so a failing endpoint never strands the console.
   */
  readonly open = computed(() => {
    const user = this.session.user();
    if (!user) return false;
    if (user.role === 'Admin') return true;
    const known = this.known();
    return !!known && known.userId === user.id && known.pending.length === 0;
  });

  /**
   * Learns, once per person, whether anything is owed. An administrator is never asked: the texts address customers
   * and rental offices, not Khadra's own staff, and the server does not judge them either.
   */
  settle(): Promise<void> {
    const user = this.session.user();
    if (!user || user.role === 'Admin') return Promise.resolve();
    if (this.known()?.userId === user.id) return Promise.resolve();
    return this.ask();
  }

  /** A request was refused for a pending consent, so a text was published since this person was last asked. */
  raise(): void {
    const user = this.session.user();
    if (!user || user.role === 'Admin') return;
    void this.ask();
  }

  /**
   * Accepts the texts the prompt showed, in the language it showed them. A newer text in force since (409) is asked
   * for afresh, so the prompt re-presents what is current rather than failing.
   */
  async accept(language: string): Promise<ConsentAnswer> {
    const user = this.session.user();
    const versionIds = this.pending().map((text) => text.versionId);
    if (!user || versionIds.length === 0) return 'accepted';

    try {
      const { requestToken } = await firstValueFrom(
        this.http.get<{ requestToken: string }>('/bff/antiforgery'),
      );
      const record = await firstValueFrom(
        this.http.post<MyLegalConsents>(
          '/api/v1/auth/me/legal-consents',
          { versionIds, language },
          { headers: { 'X-XSRF-TOKEN': requestToken } },
        ),
      );
      this.known.set({ userId: user.id, pending: record.pending ?? [] });
      return 'accepted';
    } catch (error) {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 409 &&
        error.error?.code === VERSION_NOT_CURRENT
      ) {
        await this.ask();
        return 'changed';
      }
      return 'failed';
    }
  }

  private ask(): Promise<void> {
    const user = this.session.user();
    if (!user) return Promise.resolve();

    this.asking ??= firstValueFrom(this.http.get<MyLegalConsents>('/api/v1/auth/me/legal-consents'))
      .then((record) => this.known.set({ userId: user.id, pending: record.pending ?? [] }))
      .catch(() => {
        // Not knowing is no reason to stop anybody: the server's gate still stands, and its next refusal asks again.
        // Recorded as nothing pending, so `open` lets the console work rather than wait on an answer that never comes.
        this.known.set({ userId: user.id, pending: [] });
      })
      .finally(() => (this.asking = null));
    return this.asking;
  }
}
