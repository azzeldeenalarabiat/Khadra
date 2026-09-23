import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { BOOKING_TABS, BookingListItem, BookingTab, NextBooking } from '../../core/api/bookings.api';
import { Paged } from '../../core/api/common.api';
import { snapshotProblem } from '../../core/http/problem';
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { statusLabel, statusTone } from './booking-presentation';
import { httpData } from '../../core/http/http-data';

const PAGE_SIZE = 10;

/**
 * The customer's bookings, grouped the way the server groups them (`tab` on `GET /bookings`, with the
 * counts from `/bookings/tab-counts`) — the same tabs as the app, so the two show the same truth.
 */
@Component({
  selector: 'kh-bookings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent, StatePanelComponent],
  templateUrl: './bookings.component.html',
})
export class BookingsComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  protected readonly tabs = BOOKING_TABS;

  private readonly query = toSignal(inject(ActivatedRoute).queryParamMap);
  protected readonly tab = computed<BookingTab>(() => {
    const value = this.query()?.get('tab');
    return (BOOKING_TABS as readonly string[]).includes(value ?? '') ? (value as BookingTab) : 'all';
  });
  protected readonly page = computed(() => {
    const value = Number(this.query()?.get('page'));
    return Number.isInteger(value) && value > 1 ? value : 1;
  });

  protected readonly counts = httpData<Record<string, number>>(() => '/api/v1/bookings/tab-counts');
  protected readonly next = httpData<NextBooking | null>(() => '/api/v1/bookings/next');
  protected readonly bookings = httpData<Paged<BookingListItem>>(() => ({
    url: '/api/v1/bookings',
    params: { tab: this.tab(), page: this.page(), pageSize: PAGE_SIZE },
  }));
  protected readonly problem = computed(() => (this.bookings.error() ? snapshotProblem(this.bookings.error()) : null));
  protected readonly hasAny = computed(() => (this.counts.value()?.['all'] ?? 0) > 0);

  constructor() {
    inject(SeoService).set({ title: this.i18n.t('seo.bookings.title'), noindex: true });
  }

  protected tabLabel(tab: BookingTab): string {
    return this.i18n.t(`bookings.tab.${tab}` as TranslationKey);
  }

  protected status(status: string): string {
    return statusLabel(this.i18n.t.bind(this.i18n), status);
  }

  protected tone(status: string): string {
    return statusTone(status);
  }

  protected nextLabel(reason: string): string {
    const key = `bookings.nextAction.${reason}` as TranslationKey;
    return ['AwaitingPayment', 'AwaitingDecision', 'Upcoming', 'InProgress'].includes(reason) ? this.i18n.t(key) : '';
  }

  protected total(item: BookingListItem): string {
    return this.format.money({ amount: item.totalPrice, currency: item.currency });
  }

  protected pageParams(page: number): Record<string, string> {
    return { ...(this.tab() !== 'all' ? { tab: this.tab() } : {}), ...(page > 1 ? { page: String(page) } : {}) };
  }
}
