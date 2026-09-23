import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LookupsService } from '../../core/api/lookups.service';
import { AppConfigService } from '../../core/config/app-config.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { WallClock, addDays, instantToWallClock, wallClockToInstant } from '../../core/i18n/zoned-time';
import { IconComponent } from '../icon/icon.component';

export interface SearchFormValue {
  readonly city: string | null;
  readonly from: WallClock | null;
  readonly to: WallClock | null;
  readonly text: string | null;
}

/**
 * The search card: where, when, and optionally which car. Used on the home page and above the results.
 *
 * Only two rules are checked here — both ends of the period or neither, and the return after the
 * pickup — because they are about the form, not the business. How soon a rental may start, how far
 * ahead and how long are the server's rules: the date pickers are bounded by the figures `/app-config`
 * publishes, and anything that still falls outside is refused by the API with its own reason, which
 * the results page words.
 */
@Component({
  selector: 'kh-search-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, IconComponent],
  templateUrl: './search-form.component.html',
})
export class SearchFormComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly lookups = inject(LookupsService);
  private readonly appConfig = inject(AppConfigService);

  readonly value = input<SearchFormValue>({ city: null, from: null, to: null, text: null });
  readonly compact = input(false);
  /**
   * Only the dates, for a page about one car: a city or a brand means nothing there. The value still
   * carries whatever city and text it was given, so they survive a change of dates.
   */
  readonly datesOnly = input(false);
  /** The button's words: 'search.submit' on a search, 'quote.check' beside a car. */
  readonly submitLabel = input<'search.submit' | 'quote.check'>('search.submit');
  readonly submitted = output<SearchFormValue>();

  protected readonly city = signal<string>('');
  protected readonly fromDate = signal('');
  protected readonly fromTime = signal('');
  protected readonly toDate = signal('');
  protected readonly toTime = signal('');
  protected readonly text = signal('');
  protected readonly problem = signal<'incomplete' | 'order' | null>(null);

  /** The calendar bounds the platform publishes, as dates in its own zone. */
  protected readonly bounds = computed(() => {
    const config = this.appConfig.config();
    if (!config) return null;
    const today = instantToWallClock(new Date(), config.timeZone).date;
    return { min: today, max: addDays(today, config.maxAdvanceBookingDays), zone: config.timeZone };
  });

  constructor() {
    effect(() => {
      const value = this.value();
      this.city.set(value.city ?? '');
      this.fromDate.set(value.from?.date ?? '');
      this.fromTime.set(value.from?.time ?? '');
      this.toDate.set(value.to?.date ?? '');
      this.toTime.set(value.to?.time ?? '');
      this.text.set(value.text ?? '');
      this.problem.set(null);
    });
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const parts = [this.fromDate(), this.fromTime(), this.toDate(), this.toTime()];
    const filled = parts.filter((part) => part !== '').length;
    if (filled !== 0 && filled !== 4) {
      this.problem.set('incomplete');
      return;
    }

    const from = filled === 4 ? { date: this.fromDate(), time: this.fromTime() } : null;
    const to = filled === 4 ? { date: this.toDate(), time: this.toTime() } : null;
    const zone = this.bounds()?.zone;
    if (from && to && zone) {
      const start = wallClockToInstant(from, zone);
      const end = wallClockToInstant(to, zone);
      if (start && end && end <= start) {
        this.problem.set('order');
        return;
      }
    }

    this.problem.set(null);
    this.submitted.emit({ city: this.city() || null, from, to, text: this.text().trim() || null });
  }
}
