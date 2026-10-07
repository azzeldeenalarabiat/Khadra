import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, PLATFORM_ID, computed, effect, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { NavigationEnd, Router } from '@angular/router';
import { filter, firstValueFrom } from 'rxjs';
import { ConsentGateService } from '../session/consent-gate.service';
import { SessionService } from '../session/session.service';
import { SAVE_INTENT_PARAM } from '../session/return-address';

const VEHICLE_ID = /^[0-9a-f-]{36}$/i;

/**
 * Saved cars — the SAME account list the app reads (`/customers/me/shortlist`), never a list kept in
 * the browser. A car saved here is saved on the phone, and the other way round.
 *
 * Pages tell this which cars they are showing; it asks the server which of those are saved, once per
 * batch, and keeps the answer as a set the hearts read. A heart turns at once and turns back if the
 * server refuses (the list is capped by the platform; the refusal says by how much).
 */
@Injectable({ providedIn: 'root' })
export class ShortlistService {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly consent = inject(ConsentGateService);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private readonly saved = signal<ReadonlySet<string>>(new Set());
  private readonly asked = new Set<string>();
  private readonly pending = signal<ReadonlySet<string>>(new Set());
  readonly lastRefusal = signal<{ vehicleId: string; code: string | null } | null>(null);
  private readonly router = inject(Router);
  /** A heart pressed while signed out, back in the address after sign-in (Wave 3 E5; E2E F12). */
  private readonly intent = signal<string | null>(null);

  readonly savedIds = this.saved.asReadonly();
  readonly busyIds = this.pending.asReadonly();
  readonly canSave = computed(() => this.session.isSignedIn());

  constructor() {
    // A different person (or nobody) at the keyboard: forget whose hearts these were.
    effect(() => {
      this.session.user();
      this.saved.set(new Set());
      this.asked.clear();
    });

    if (this.isBrowser) {
      this.router.events.pipe(filter((event) => event instanceof NavigationEnd)).subscribe(() => {
        const id = this.router.parseUrl(this.router.url).queryParams[SAVE_INTENT_PARAM];
        this.intent.set(typeof id === 'string' && VEHICLE_ID.test(id) ? id.toLowerCase() : null);
      });
      // Once somebody is signed in, the car they chose before signing in is saved, once, and the address forgets it —
      // and only once they are known to owe no legal consent (Wave 4, W4-8), or the save would only be refused.
      effect(() => {
        const vehicleId = this.intent();
        if (!vehicleId || !this.session.isSignedIn() || !this.consent.clear()) return;
        this.intent.set(null);
        void this.saveIntended(vehicleId);
      });
    }
  }

  /** Saves a car, whatever it was before: a PUT, so sending it twice saves it once. Never the toggle. */
  async save(vehicleId: string): Promise<void> {
    if (this.pending().has(vehicleId)) return;
    this.setSaved(vehicleId, true);
    this.pending.update((current) => new Set([...current, vehicleId]));
    this.lastRefusal.set(null);
    try {
      await firstValueFrom(this.http.put(`/api/v1/customers/me/shortlist/${vehicleId}`, {}));
    } catch (error) {
      this.setSaved(vehicleId, false);
      const code = error instanceof HttpErrorResponse ? ((error.error?.code as string | undefined) ?? null) : null;
      this.lastRefusal.set({ vehicleId, code });
    } finally {
      this.pending.update((current) => {
        const next = new Set(current);
        next.delete(vehicleId);
        return next;
      });
    }
  }

  /** The visitor has read why a heart did not hold. */
  dismissRefusal(): void {
    this.lastRefusal.set(null);
  }

  private async saveIntended(vehicleId: string): Promise<void> {
    void this.router.navigate([], { queryParams: { [SAVE_INTENT_PARAM]: null }, queryParamsHandling: 'merge', replaceUrl: true });
    await this.save(vehicleId);
  }

  isSaved(vehicleId: string): boolean {
    return this.saved().has(vehicleId);
  }

  /** Learn which of these cars are saved. Asks only about ids not asked about before. */
  async track(vehicleIds: readonly string[]): Promise<void> {
    if (!this.isBrowser || !this.session.isSignedIn()) return;
    const fresh = vehicleIds.filter((id) => !this.asked.has(id));
    if (fresh.length === 0) return;
    fresh.forEach((id) => this.asked.add(id));

    let params = new HttpParams();
    fresh.forEach((id) => (params = params.append('vehicleId', id)));
    try {
      const savedIds = await firstValueFrom(
        this.http.get<string[]>('/api/v1/customers/me/shortlist/membership', { params }),
      );
      this.saved.update((current) => new Set([...current, ...savedIds.map((id) => id.toLowerCase())]));
    } catch {
      fresh.forEach((id) => this.asked.delete(id));
    }
  }

  async toggle(vehicleId: string): Promise<void> {
    if (this.pending().has(vehicleId)) return;
    const wasSaved = this.isSaved(vehicleId);
    this.setSaved(vehicleId, !wasSaved);
    this.pending.update((current) => new Set([...current, vehicleId]));
    this.lastRefusal.set(null);
    try {
      const url = `/api/v1/customers/me/shortlist/${vehicleId}`;
      await firstValueFrom(wasSaved ? this.http.delete(url) : this.http.put(url, {}));
    } catch (error) {
      this.setSaved(vehicleId, wasSaved);
      const code = error instanceof HttpErrorResponse ? ((error.error?.code as string | undefined) ?? null) : null;
      this.lastRefusal.set({ vehicleId, code });
    } finally {
      this.pending.update((current) => {
        const next = new Set(current);
        next.delete(vehicleId);
        return next;
      });
    }
  }

  /** The saved page removed or listed something itself: keep the hearts in step with it. */
  setSaved(vehicleId: string, isSaved: boolean): void {
    this.saved.update((current) => {
      const next = new Set(current);
      if (isSaved) next.add(vehicleId);
      else next.delete(vehicleId);
      return next;
    });
  }
}
