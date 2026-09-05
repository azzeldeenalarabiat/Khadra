import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** A city or a car type. The two share a shape, which is why one screen serves both. */
export interface LookupEntry {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
  readonly displayOrder: number;
  readonly createdAt: string;
  /** Cities only, and optional: a city that has not been pinned yet is a real state. */
  readonly centreLatitude: number | null;
  readonly centreLongitude: number | null;
}

export type LookupKind = 'car-types' | 'cities';

@Injectable({ providedIn: 'root' })
export class LookupsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/admin/lookups';

  /** Which list the screen is showing. Set by the route, so the two screens share one resource. */
  readonly kind = signal<LookupKind | null>(null);

  /**
   * The car types a dealer can list a car under.
   *
   * A different endpoint from the admin one above and deliberately so: this is the plain
   * `GET /api/v1/car-types`, open to any signed-in client and returning only the ACTIVE entries, so
   * a type an administrator has retired stops being offered on new cars while every car already
   * pointing at it keeps reading correctly.
   */
  readonly carTypes = httpResource<readonly LookupEntry[]>(() => '/api/v1/car-types');

  readonly entries = httpResource<readonly LookupEntry[]>(() => {
    const kind = this.kind();
    return kind ? `${this.base}/${kind}` : undefined;
  });

  private async withToken<T>(call: (token: string) => Promise<T>): Promise<T> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/bff/antiforgery'));
    return call(token.requestToken);
  }

  create(kind: LookupKind, body: Record<string, unknown>): Promise<LookupEntry> {
    return this.withToken((token) =>
      firstValueFrom(
        this.http.post<LookupEntry>(`${this.base}/${kind}`, body, {
          headers: { 'X-XSRF-TOKEN': token },
        }),
      ),
    );
  }

  rename(kind: LookupKind, id: string, nameEn: string, nameAr: string): Promise<LookupEntry> {
    return this.withToken((token) =>
      firstValueFrom(
        this.http.post<LookupEntry>(
          `${this.base}/${kind}/${id}/rename`,
          { nameEn, nameAr },
          { headers: { 'X-XSRF-TOKEN': token } },
        ),
      ),
    );
  }

  setActive(kind: LookupKind, id: string, isActive: boolean): Promise<LookupEntry> {
    return this.withToken((token) =>
      firstValueFrom(
        this.http.post<LookupEntry>(
          `${this.base}/${kind}/${id}/${isActive ? 'activate' : 'deactivate'}`,
          {},
          { headers: { 'X-XSRF-TOKEN': token } },
        ),
      ),
    );
  }

  refresh(): void {
    this.entries.reload();
  }
}
