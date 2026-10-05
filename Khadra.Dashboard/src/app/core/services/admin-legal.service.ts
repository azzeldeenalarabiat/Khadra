import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  LegalDocumentPreview,
  LegalDocumentVersion,
  LegalDocumentVersionSummary,
  LegalTextsRequest,
} from '../models/legal.api';

interface Page<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly hasPrevious: boolean;
  readonly hasNext: boolean;
}

/**
 * The legal texts' administration (Wave 2 G1): every published version, one in full, and the two writes — the
 * preview, which writes nothing, and the publish, which is permanent.
 */
@Injectable({ providedIn: 'root' })
export class AdminLegalService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/admin/legal-documents';

  /** The page of the version list on screen. */
  readonly page = signal(1);

  /** Which version is open in full, or null. */
  readonly viewing = signal<string | null>(null);

  readonly versions = httpResource<Page<LegalDocumentVersionSummary>>(() => ({
    url: this.base,
    params: { page: this.page(), pageSize: 20 },
  }));

  readonly version = httpResource<LegalDocumentVersion>(() => {
    const id = this.viewing();
    return id ? `${this.base}/${id}` : undefined;
  });

  private async withToken<T>(call: (token: string) => Promise<T>): Promise<T> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/bff/antiforgery'));
    return call(token.requestToken);
  }

  /** What publishing would publish, rendered as the public page shows it. Nothing is written. */
  preview(body: LegalTextsRequest): Promise<LegalDocumentPreview> {
    return this.withToken((token) =>
      firstValueFrom(
        this.http.post<LegalDocumentPreview>(`${this.base}/preview`, body, {
          headers: { 'X-XSRF-TOKEN': token },
        }),
      ),
    );
  }

  /** Publishes a version: in force at once, and permanent. */
  publish(body: LegalTextsRequest): Promise<LegalDocumentVersion> {
    return this.withToken((token) =>
      firstValueFrom(
        this.http.post<LegalDocumentVersion>(this.base, body, { headers: { 'X-XSRF-TOKEN': token } }),
      ),
    );
  }

  refresh(): void {
    this.versions.reload();
  }
}
