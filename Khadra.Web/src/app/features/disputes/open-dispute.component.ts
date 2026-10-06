import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Booking } from '../../core/api/bookings.api';
import { AppConfigService } from '../../core/config/app-config.service';
import { httpData } from '../../core/http/http-data';
import { ProblemSnapshot, snapshotProblem } from '../../core/http/problem';
import { problemText } from '../../core/http/problem-text';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { DisputeApi } from './dispute-api';
import { EvidenceDraft, evidenceRefusalText } from './evidence-draft';

/**
 * Opening a dispute on the website, as the app does (Wave 3 C4; E2E F31, pre-launch item 146).
 *
 * Offered on a booking only while the server says a dispute can be opened and none is open (`canBeDisputed`, and no
 * `liveDisputeId`: the app's own rule), with the window's end as the booking carries it. The reason is free text, as in
 * the app; evidence is uploaded first and quoted by key. Every refusal is the server's, worded here in both languages.
 */
@Component({
  selector: 'kh-open-dispute',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent, StatePanelComponent],
  templateUrl: './open-dispute.component.html',
})
export class OpenDisputeComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  private readonly api = inject(DisputeApi);
  private readonly router = inject(Router);
  private readonly appConfig = inject(AppConfigService);

  readonly bookingId = input<string>('');
  protected readonly booking = httpData<Booking>(() => {
    const id = this.bookingId();
    return /^[0-9a-f-]{36}$/i.test(id) ? `/api/v1/bookings/${id}` : undefined;
  });
  protected readonly loadProblem = computed(() => (this.booking.error() ? snapshotProblem(this.booking.error()) : null));
  protected readonly notFound = computed(
    () => !/^[0-9a-f-]{36}$/i.test(this.bookingId()) || this.loadProblem()?.status === 404,
  );
  /** Whether the server says a dispute may be opened now: the window is open and none is live. */
  protected readonly canOpen = computed(() => {
    const b = this.booking.value();
    return !!b && b.canBeDisputed && !b.liveDisputeId;
  });

  protected readonly limits = computed(() => this.appConfig.config()?.documents ?? null);
  protected readonly accept = computed(() => (this.limits()?.allowedContentTypes ?? []).join(','));

  protected readonly reason = signal('');
  protected readonly evidence = new EvidenceDraft();
  protected readonly busy = signal(false);
  private readonly reasonMissing = signal(false);
  /** A refusal held as facts, worded when shown, so switching language re-words it. */
  private readonly problem = signal<ProblemSnapshot | null>(null);

  protected readonly problemText = computed(() => {
    if (this.reasonMissing()) return this.i18n.t('dispute.open.reasonRequired');
    const refused = evidenceRefusalText(this.evidence.refused(), this.limits(), this.i18n.t.bind(this.i18n), (bytes) =>
      `${this.format.number(bytes / (1024 * 1024), 0)} MB`,
    );
    if (refused) return refused;
    const problem = this.problem();
    return problem ? problemText(problem, this.i18n.t.bind(this.i18n), this.i18n.language(), this.appConfig.config()) : null;
  });

  constructor() {
    const seo = inject(SeoService);
    effect(() => seo.set({ title: this.i18n.t('dispute.open.title'), noindex: true }));
  }

  protected setReason(event: Event): void {
    this.reason.set((event.target as HTMLTextAreaElement).value);
    this.reasonMissing.set(false);
  }

  protected choose(event: Event): void {
    const input = event.target as HTMLInputElement;
    const chosen = [...(input.files ?? [])];
    input.value = '';
    this.evidence.choose(chosen, this.limits());
  }

  protected async submit(): Promise<void> {
    const booking = this.booking.value();
    if (!booking || this.busy()) return;
    const reason = this.reason().trim();
    this.problem.set(null);
    if (!reason) {
      this.reasonMissing.set(true);
      return;
    }
    this.busy.set(true);
    try {
      const keys: string[] = [];
      for (const file of this.evidence.files()) keys.push(await this.api.attach(booking.bookingId, file));
      const opened = await this.api.open(booking.bookingId, reason, keys);
      await this.router.navigate(this.i18n.link('disputes', opened.ticketId), { state: { opened: true } });
    } catch (error) {
      this.problem.set(snapshotProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
