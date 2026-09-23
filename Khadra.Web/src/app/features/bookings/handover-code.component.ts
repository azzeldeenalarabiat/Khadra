import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { HandoverCode } from '../../core/api/bookings.api';
import { ProblemSnapshot, snapshotProblem } from '../../core/http/problem';
import { I18nService } from '../../core/i18n/i18n.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { clockCountdown, countdownParts } from './countdown';

/**
 * The code a customer shows at the counter so the office can verify, before the keys change hands,
 * that this booking is theirs: six digits and the same thing as a QR code, with the time it stops
 * working. A new code cancels the old one (the server's rule). The code is a live credential, so it is
 * never stored — not in the address, not in the browser — and it is dropped when the panel closes.
 *
 * The endpoint belongs to the handover-code work; on a server without it the panel says so plainly
 * rather than showing a broken button.
 */
@Component({
  selector: 'kh-handover-code',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent],
  templateUrl: './handover-code.component.html',
})
export class HandoverCodeComponent {
  protected readonly i18n = inject(I18nService);
  private readonly http = inject(HttpClient);

  readonly bookingId = input.required<string>();
  readonly isReturn = input(false);
  readonly closed = output<void>();

  protected readonly code = signal<HandoverCode | null>(null);
  protected readonly qr = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ProblemSnapshot | null>(null);
  protected readonly unavailable = computed(() => {
    const problem = this.problem();
    // A 404 with no platform code is the route itself missing, not this booking.
    return problem !== null && problem.status === 404 && !problem.code;
  });

  private readonly now = signal(Date.now());
  protected readonly remaining = computed(() => countdownParts(this.code()?.expiresAt, this.now()));
  protected readonly clock = computed(() => {
    const parts = this.remaining();
    return parts ? clockCountdown(parts) : '';
  });
  protected readonly digits = computed(() => {
    const code = this.code()?.code ?? '';
    return code.length === 6 ? `${code.slice(0, 3)} ${code.slice(3)}` : code;
  });

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
    void this.issue();
  }

  protected async issue(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      const code = await firstValueFrom(this.http.post<HandoverCode>(`/api/v1/bookings/${this.bookingId()}/handover-code`, {}));
      this.code.set(code);
      this.now.set(Date.now());
      const QRCode = await import('qrcode');
      this.qr.set(await QRCode.toDataURL(code.qrPayload, { margin: 1, width: 440, errorCorrectionLevel: 'M' }));
    } catch (error) {
      this.code.set(null);
      this.qr.set(null);
      this.problem.set(snapshotProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
