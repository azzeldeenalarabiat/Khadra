import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { HandoverCode } from '../../core/api/bookings.api';
import { AppConfigService } from '../../core/config/app-config.service';
import { ProblemSnapshot, snapshotProblem } from '../../core/http/problem';
import { problemText } from '../../core/http/problem-text';
import { I18nService } from '../../core/i18n/i18n.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { clockCountdown, countdownParts } from './countdown';

/**
 * The code a customer shows at the counter (`POST /bookings/{id}/handover-code`): six digits and the
 * same thing as a QR, and how long it stays valid. Which handover it proves — pickup while Confirmed,
 * return while PickedUp — is the SERVER's decision; the panel only titles itself from the answer.
 *
 * Asking again replaces the code and the previous one stops working (the server's rule). The office
 * scans or types it and records the handover; the booking page watches the booking while this panel is
 * open and closes it the moment the status moves on — the customer presses nothing. The code is a live
 * credential: it is never stored, not in the address and not in the browser, and dropped on close.
 */
@Component({
  selector: 'kh-handover-code',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent],
  templateUrl: './handover-code.component.html',
})
export class HandoverCodeComponent implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly http = inject(HttpClient);
  private readonly appConfig = inject(AppConfigService);

  readonly bookingId = input.required<string>();
  /**
   * The handover the booking page expects, from the booking's status. Only titles the panel until the
   * server answers (or when it fails); the code the server issues says which handover it proves.
   */
  readonly expected = input<'Pickup' | 'Return' | null>(null);
  readonly closed = output<void>();

  protected readonly code = signal<HandoverCode | null>(null);
  protected readonly qr = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ProblemSnapshot | null>(null);

  /** `handover.not_available`: the booking is no longer at a handover (it moved on, or ended). */
  protected readonly notAvailable = computed(() => this.problem()?.code === 'handover.not_available');
  protected readonly problemMessage = computed(() => {
    const problem = this.problem();
    return problem ? problemText(problem, this.i18n.t.bind(this.i18n), this.i18n.language(), this.appConfig.config()) : null;
  });

  protected readonly isReturn = computed(() => (this.code()?.type ?? this.expected()) === 'Return');
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
  }

  // Not in the constructor: a required input has no value there, and reading it threw — which the
  // catch below reported as "Khadra is not answering", on every first open of the panel.
  ngOnInit(): void {
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
