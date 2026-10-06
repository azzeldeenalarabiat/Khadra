import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** `POST /api/v1/disputes/evidence/upload-url`: where to PUT the file, and the key to quote afterwards. */
interface EvidenceTicket {
  readonly uploadUrl: string;
  readonly storageKey: string;
  readonly expiresAt: string;
}

/** What opening a dispute answers with: only what this site navigates and speaks by. */
export interface OpenedDispute {
  readonly ticketId: string;
  readonly slaDeadline: string;
}

/**
 * The customer's side of a dispute on the website, as in the app (Wave 3 C4; E2E F31, pre-launch item 146): open one,
 * add to it, withdraw it.
 *
 * Evidence travels the app's way: an upload address first, then the file's own bytes PUT there (never multipart), and
 * only the storage keys reach the dispute. Every call goes through the customer BFF, which adds the session and checks
 * the antiforgery token; the server judges every file and every step again.
 */
@Injectable({ providedIn: 'root' })
export class DisputeApi {
  private readonly http = inject(HttpClient);

  /** Uploads one file as evidence for the booking, and returns the key the dispute quotes it by. */
  async attach(bookingId: string, file: File): Promise<string> {
    const contentType = file.type;
    const ticket = await firstValueFrom(
      this.http.post<EvidenceTicket>('/api/v1/disputes/evidence/upload-url', {
        bookingId,
        fileName: file.name,
        contentType,
      }),
    );
    // The declared type, set explicitly: the server compares it exactly against the ticket it minted.
    await firstValueFrom(this.http.put(ticket.uploadUrl, file, { headers: { 'Content-Type': contentType } }));
    return ticket.storageKey;
  }

  open(bookingId: string, reason: string, evidenceKeys: readonly string[]): Promise<OpenedDispute> {
    return firstValueFrom(this.http.post<OpenedDispute>('/api/v1/disputes', { bookingId, reason, evidenceKeys }));
  }

  addStatement(ticketId: string, body: string, evidenceKeys: readonly string[]): Promise<unknown> {
    return firstValueFrom(this.http.post(`/api/v1/disputes/${ticketId}/statements`, { body, evidenceKeys }));
  }

  withdraw(ticketId: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`/api/v1/disputes/${ticketId}/withdraw`, {}));
  }
}
