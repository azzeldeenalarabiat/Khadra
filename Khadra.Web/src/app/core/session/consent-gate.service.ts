import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, computed, effect, inject, signal, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LegalConfigDocument, MyLegalConsents } from '../api/legal.api';
import { SessionService } from './session.service';

/** The API's refusal while a legal text in force waits for this person's consent (Wave 4, W4-8). */
export const CONSENT_PENDING = 'legal.consent_pending';

/** A text was replaced between being shown and being accepted: up to a few minutes after a publish. */
export const VERSION_NOT_CURRENT = 'legal.version_not_current';

/** A registration that did not accept a text in force, because the page never showed it. */
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
 * Whether the signed-in customer must accept a legal text before the website will answer them (Wave 4, W4-8; owner
 * D6: blocked, not a banner).
 *
 * The SERVER is the gate: while a text in force waits for this person's consent, every request they make with their
 * session is refused with 403 `legal.consent_pending`, except the few that resolve it. This is how the website says so
 * once — one prompt in place of the page — rather than as every page failing on its own. It asks as soon as somebody is
 * signed in, and is told again by any refusal, which is how a text published while the page was open arrives.
 *
 * Everything that would only be refused while it is pending — the unread count's poll, a car saved before signing
 * in — waits on {@link blocked} or {@link clear}, and goes back to the server the moment the texts are accepted.
 */
@Injectable({ providedIn: 'root' })
export class ConsentGateService {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private readonly known = signal<Known | null>(null);
  private asking: Promise<void> | null = null;

  /** This person's answer, or null while they have not been asked. */
  private readonly mine = computed(() => {
    const known = this.known();
    const user = this.session.user();
    return known && user && known.userId === user.id ? known : null;
  });

  /** The texts in force still to accept, with their pages; empty when nothing is owed, or nothing is known yet. */
  readonly pending = computed<readonly LegalConfigDocument[]>(() => this.mine()?.pending ?? []);

  /** While true the website shows the prompt in place of the page, and asks nothing that would be refused. */
  readonly blocked = computed(() => this.pending().length > 0);

  /** Asked, and nothing owed: what a write made on somebody's behalf, rather than at their click, waits for. */
  readonly clear = computed(() => this.mine()?.pending.length === 0);

  constructor() {
    // The website has no guard in front of most pages, so being signed in is what starts the question. The server
    // never renders a session, so it never asks.
    effect(() => {
      const user = this.session.user();
      if (!this.isBrowser || !user) return;
      untracked(() => void this.settle());
    });
  }

  /** Learns, once per person, whether anything is owed. */
  settle(): Promise<void> {
    const user = this.session.user();
    if (!user) return Promise.resolve();
    if (this.known()?.userId === user.id) return Promise.resolve();
    return this.ask();
  }

  /** A request was refused for a pending consent, so a text was published since this person was last asked. */
  raise(): void {
    if (this.session.user()) void this.ask();
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
      const record = await firstValueFrom(
        this.http.post<MyLegalConsents>('/api/v1/auth/me/legal-consents', { versionIds, language }),
      );
      this.known.set({ userId: user.id, pending: record.pending ?? [] });
      return 'accepted';
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 409 && error.error?.code === VERSION_NOT_CURRENT) {
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
        // Left unknown (the advisor's review), as the console leaves it: nothing is blocked, because the server's gate
        // still stands and its next refusal asks again; and nothing is CLEAR, so what waits on `clear` — a car saved
        // before signing in — keeps waiting rather than being sent to a refusal and forgotten.
      })
      .finally(() => (this.asking = null));
    return this.asking;
  }
}
