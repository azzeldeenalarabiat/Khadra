import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Dispute, EvidenceUpload } from '../models/disputes.api';

/**
 * A party's side of a dispute (spec 3.3): open one from a booking, add to it, withdraw it. The Admin's
 * decision arrives on the same ticket. All calls go through the BFF.
 */
@Injectable({ providedIn: 'root' })
export class DealerDisputesService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/disputes';

  readonly viewing = signal<string | null>(null);

  readonly dispute = httpResource<Dispute>(() => {
    const id = this.viewing();
    return id ? `${this.base}/${id}` : undefined;
  });

  open(bookingId: string, reason: string, evidenceKeys: readonly string[]): Promise<Dispute> {
    return firstValueFrom(this.http.post<Dispute>(this.base, { bookingId, reason, evidenceKeys }));
  }

  addStatement(ticketId: string, body: string, evidenceKeys: readonly string[]): Promise<Dispute> {
    return firstValueFrom(
      this.http.post<Dispute>(`${this.base}/${ticketId}/statements`, { body, evidenceKeys }),
    );
  }

  withdraw(ticketId: string): Promise<Dispute> {
    return firstValueFrom(this.http.post<Dispute>(`${this.base}/${ticketId}/withdraw`, {}));
  }

  /** Evidence is uploaded before the ticket exists, scoped to the booking; the ticket quotes the keys. */
  async uploadEvidence(bookingId: string, file: File): Promise<string> {
    const ticket = await firstValueFrom(
      this.http.post<EvidenceUpload>(`${this.base}/evidence/upload-url`, {
        bookingId,
        fileName: file.name,
        contentType: file.type,
      }),
    );
    await firstValueFrom(
      this.http.put(ticket.uploadUrl, file, { headers: { 'Content-Type': file.type } }),
    );
    return ticket.storageKey;
  }

  refresh(): void {
    this.dispute.reload();
  }
}
