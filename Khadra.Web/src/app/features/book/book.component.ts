import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Booking } from '../../core/api/bookings.api';
import { CatalogueVehicle, RentalQuote } from '../../core/api/catalogue.api';
import { CustomerDocuments } from '../../core/api/documents.api';
import { AppConfigService } from '../../core/config/app-config.service';
import { ProblemSnapshot, snapshotProblem } from '../../core/http/problem';
import { problemText } from '../../core/http/problem-text';
import { TranslationKey } from '../../core/i18n/en';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { wallClockToInstant } from '../../core/i18n/zoned-time';
import { slugFor } from '../../core/routing/slug';
import { SeoService } from '../../core/seo/seo.service';
import { SessionService } from '../../core/session/session.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { MapComponent, MapPoint } from '../../shared/map/map.component';
import { SearchFormComponent, SearchFormValue } from '../../shared/search-form/search-form.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { EMPTY_SEARCH, parseSearch, searchToParams } from '../cars/car-search';
import { httpData } from '../../core/http/http-data';

/**
 * Requesting a car. Everything the customer agrees to is the server's: the quote prices it (days,
 * delivery fee, deposit), the terms come with the quote, and the request sends only the car, the
 * period, the method and — for delivery — the point. No price is ever sent.
 *
 * What would make the server refuse is said BEFORE the button, from the server's own answers: an
 * unverified email (the session says so) and incomplete documents (the documents endpoint says so).
 * The server checks both again when the request arrives, and its refusal is shown if it does.
 */
@Component({
  selector: 'kh-book',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent, MapComponent, SearchFormComponent, StatePanelComponent],
  templateUrl: './book.component.html',
})
export class BookComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  protected readonly session = inject(SessionService);
  private readonly appConfig = inject(AppConfigService);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  readonly vehicleId = input<string>('');
  protected readonly params = toSignal(inject(ActivatedRoute).queryParams, { initialValue: {} });

  protected readonly period = computed(() => {
    const search = parseSearch(this.params());
    return search.from && search.to ? { from: search.from, to: search.to } : null;
  });
  private readonly instants = computed(() => {
    const period = this.period();
    const zone = this.format.timeZone();
    if (!period || !zone) return null;
    const pickup = wallClockToInstant(period.from, zone);
    const dropoff = wallClockToInstant(period.to, zone);
    return pickup && dropoff ? { pickupAt: pickup.toISOString(), returnAt: dropoff.toISOString() } : null;
  });

  protected readonly method = signal<'SelfPickup' | 'Delivery'>('SelfPickup');
  protected readonly point = signal<MapPoint | null>(null);

  protected readonly car = httpData<CatalogueVehicle>(() => {
    const id = this.vehicleId();
    return /^[0-9a-f-]{36}$/i.test(id) ? `/api/v1/vehicles/${id}` : undefined;
  });
  protected readonly documents = httpData<CustomerDocuments>(() => '/api/v1/customers/me/documents');

  protected readonly quote = httpData<RentalQuote>(() => {
    const car = this.car.value();
    const when = this.instants();
    if (!car || !when) return undefined;
    const delivery = this.method() === 'Delivery';
    const point = this.point();
    if (delivery && !point) return undefined;
    return {
      url: `/api/v1/vehicles/${car.vehicleId}/quote`,
      params: {
        ...when,
        pickupMethod: this.method(),
        ...(delivery && point ? { latitude: point.latitude, longitude: point.longitude } : {}),
      },
    };
  });

  protected readonly carProblem = computed(() => (this.car.error() ? snapshotProblem(this.car.error()) : null));
  protected readonly quoteProblem = computed(() => (this.quote.error() ? snapshotProblem(this.quote.error()) : null));
  protected readonly deliveryOffered = computed(() => {
    const car = this.car.value();
    return !!car && car.gallery.delivery.isEnabled && car.isDeliveryEligible;
  });
  protected readonly carLink = computed(() => {
    const car = this.car.value();
    return car ? this.i18n.link('cars', slugFor(car.vehicleId, car.make, car.model, car.year)) : this.i18n.link('cars');
  });

  protected readonly emailUnverified = computed(() => this.session.user()?.isEmailVerified === false);
  protected readonly documentsIncomplete = computed(() => this.documents.value()?.isComplete === false);

  protected readonly busy = signal(false);
  protected readonly problem = signal<ProblemSnapshot | null>(null);
  protected readonly refusal = computed(() => {
    const problem = this.problem();
    return problem ? this.wordRefusal(problem) : null;
  });
  protected readonly quoteRefusal = computed(() => {
    const problem = this.quoteProblem();
    return problem ? this.wordRefusal(problem, 'quote.failed') : null;
  });
  private readonly refusalKeys: Record<string, true> = Object.fromEntries(
    [
      'booking.documents_incomplete',
      'booking.email_not_verified',
      'booking.vehicle_unavailable',
      'booking.delivery_out_of_range',
      'booking.delivery_location_required',
      'booking.pickup_outside_opening_hours',
      'booking.return_outside_opening_hours',
      'booking.account_cannot_book',
    ].map((code) => [`book.refusal.${code}`, true]),
  );

  /** A booking refusal in the reader's words: the codes this page knows, then the shared table. */
  private wordRefusal(problem: ProblemSnapshot, fallback?: TranslationKey): string {
    const key = `book.refusal.${problem.code}` as TranslationKey;
    if (problem.code && key in this.refusalKeys) return this.i18n.t(key);
    if (fallback && problem.status === 400) return this.i18n.t(fallback);
    return problemText(problem, this.i18n.t.bind(this.i18n), this.i18n.language(), this.appConfig.config());
  }

  protected readonly canSubmit = computed(
    () =>
      !this.busy() &&
      this.quote.value()?.isAvailable === true &&
      !(this.method() === 'Delivery' && !this.point()),
  );

  protected readonly formValue = computed<SearchFormValue>(() => ({
    city: null,
    from: this.period()?.from ?? null,
    to: this.period()?.to ?? null,
    text: null,
  }));

  constructor() {
    inject(SeoService).set({ title: this.i18n.t('seo.book.title'), noindex: true });
  }

  protected choose(value: SearchFormValue): void {
    this.problem.set(null);
    void this.router.navigate([], {
      queryParams: searchToParams({ ...EMPTY_SEARCH, from: value.from, to: value.to }),
      replaceUrl: true,
    });
  }

  protected setMethod(method: 'SelfPickup' | 'Delivery'): void {
    this.method.set(method);
    this.problem.set(null);
    if (method === 'SelfPickup') this.point.set(null);
  }

  protected percent(value: number): string {
    return `${this.format.number(value)}%`;
  }

  protected async submit(): Promise<void> {
    const car = this.car.value();
    const when = this.instants();
    if (!car || !when || !this.canSubmit()) return;
    this.busy.set(true);
    this.problem.set(null);
    const point = this.point();
    try {
      const booking = await firstValueFrom(
        this.http.post<Booking>('/api/v1/bookings', {
          vehicleId: car.vehicleId,
          pickupAt: when.pickupAt,
          returnAt: when.returnAt,
          pickupMethod: this.method(),
          latitude: this.method() === 'Delivery' ? (point?.latitude ?? null) : null,
          longitude: this.method() === 'Delivery' ? (point?.longitude ?? null) : null,
        }),
      );
      void this.router.navigate(this.i18n.link('bookings', booking.bookingId), { queryParams: { created: 1 }, replaceUrl: true });
    } catch (error) {
      const problem = snapshotProblem(error);
      this.problem.set(problem);
      // Someone else booked it first: ask again, so the page shows the car as taken.
      if (problem.code === 'booking.vehicle_unavailable') this.quote.reload();
      this.busy.set(false);
    }
  }
}
