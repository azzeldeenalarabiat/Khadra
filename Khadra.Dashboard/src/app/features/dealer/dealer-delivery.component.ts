import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';

/**
 * Delivery (spec 4.4, design `isDelivery`).
 *
 * All three are the gallery's: whether they deliver, how far, and what they charge. The fee used to
 * be the platform's and was shown as a fact rather than a field; the owner moved it here, to the
 * business that actually drives the car and collects the cash.
 *
 * Saving is explicit -- a radius is a promise to drive somewhere and a fee is a price -- and the
 * toggle should not commit either on a keystroke.
 */
@Component({
  selector: 'kh-dealer-delivery',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-delivery.component.html',
  imports: [IconComponent],
})
export class DealerDeliveryComponent {
  protected readonly t = inject(I18nService).t;
  private readonly service = inject(DealerConsoleService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly resource = this.service.delivery;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly me = this.service.me;
  protected readonly dealer = loaded(this.me);
  protected readonly settings = computed(() => this.data() ?? null);

  protected readonly enabled = signal(false);
  protected readonly radius = signal(0);
  /** Null while unanswered, so “not set yet” and “free delivery” stay different answers. */
  protected readonly fee = signal<number | null>(null);
  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);

  constructor() {
    // Seed the form from the server each time it answers; the form is a draft of THAT answer.
    effect(() => {
      const s = this.settings();
      if (!s) return;
      this.enabled.set(s.isEnabled);
      this.radius.set(s.radiusKm);
      this.fee.set(s.fee?.amount ?? null);
    });
  }

  protected readonly canEdit = computed(
    () => !!this.dealer()?.isOwner && !!this.dealer()?.canTrade,
  );

  protected readonly dirty = computed(() => {
    const s = this.settings();
    if (!s) return false;
    if (s.isEnabled !== this.enabled()) return true;
    if (!this.enabled()) return false;
    return s.radiusKm !== this.radius() || (s.fee?.amount ?? null) !== this.fee();
  });

  /**
   * Unanswered is invalid; zero is not.
   *
   * A gallery may genuinely deliver free of charge, so 0 has to be a saveable answer — which is
   * exactly why the field starts null rather than 0. The bound is the server’s own.
   */
  protected readonly feeInvalid = computed(() => {
    if (!this.enabled()) return false;
    const fee = this.fee();
    const max = this.settings()?.maxFee;
    return fee === null || fee < 0 || (max !== undefined && fee > max);
  });

  protected readonly radiusInvalid = computed(() => {
    const s = this.settings();
    return this.enabled() && (!(this.radius() > 0) || (s ? this.radius() > s.maxRadiusKm : false));
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    return 'Delivery settings could not be loaded. Nothing has been changed.';
  });

  protected setRadius(event: Event): void {
    this.radius.set(Number((event.target as HTMLInputElement).value));
  }

  protected setFee(event: Event): void {
    const raw = (event.target as HTMLInputElement).value;
    this.fee.set(raw.trim() === '' ? null : Number(raw));
  }

  protected async save(): Promise<void> {
    if (this.busy() || this.radiusInvalid() || this.feeInvalid()) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      await this.service.updateDelivery(
        this.enabled(),
        this.enabled() ? this.radius() : 0,
        this.enabled() ? this.fee() : null,
      );
      this.resource.reload();
      this.service.refreshMe();
      this.ui.showToast(
        this.enabled() ? 'Delivery switched on' : 'Delivery switched off',
        this.enabled()
          ? `Customers within ${this.radius()} km of your location can ask for delivery.`
          : 'Customers will collect from your location only.',
      );
    } catch (error) {
      const p = error as { error?: { code?: string; title?: string } };
      this.problem.set(
        p.error?.code === 'dealer.invalid_delivery_radius'
          ? `The radius must be between 0 and ${this.settings()?.maxRadiusKm ?? 200} km.`
          : (p.error?.title ?? 'The service did not respond. Nothing has been changed.'),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
