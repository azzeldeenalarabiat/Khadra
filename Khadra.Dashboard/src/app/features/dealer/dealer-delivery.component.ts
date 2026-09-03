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
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * Delivery (spec 4.4, design `isDelivery`).
 *
 * Two things are the dealer's: whether they deliver, and how far. The fee is the platform's and is
 * shown as a fact, not a field. Saving is explicit -- a radius is a promise to drive somewhere, and
 * the toggle should not commit it on every keystroke.
 */
@Component({
  selector: 'kh-dealer-delivery',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-delivery.component.html',
  imports: [IconComponent],
})
export class DealerDeliveryComponent {
  private readonly service = inject(DealerConsoleService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly resource = this.service.delivery;
  protected readonly me = this.service.me;
  protected readonly settings = computed(() => this.resource.value() ?? null);

  protected readonly enabled = signal(false);
  protected readonly radius = signal(0);
  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);

  constructor() {
    // Seed the form from the server each time it answers; the form is a draft of THAT answer.
    effect(() => {
      const s = this.settings();
      if (!s) return;
      this.enabled.set(s.isEnabled);
      this.radius.set(s.radiusKm);
    });
  }

  protected readonly canEdit = computed(
    () => !!this.me.value()?.isOwner && !!this.me.value()?.canTrade,
  );

  protected readonly dirty = computed(() => {
    const s = this.settings();
    return (
      !!s && (s.isEnabled !== this.enabled() || (this.enabled() && s.radiusKm !== this.radius()))
    );
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

  protected async save(): Promise<void> {
    if (this.busy() || this.radiusInvalid()) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      await this.service.updateDelivery(this.enabled(), this.enabled() ? this.radius() : 0);
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
