import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, PLATFORM_ID, computed, effect, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { SessionService } from '../session/session.service';

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
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private readonly saved = signal<ReadonlySet<string>>(new Set());
  private readonly asked = new Set<string>();
  private readonly pending = signal<ReadonlySet<string>>(new Set());
  readonly lastRefusal = signal<{ vehicleId: string; code: string | null } | null>(null);

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
