import { isPlatformBrowser } from '@angular/common';
import { ChangeDetectionStrategy, Component, PLATFORM_ID, computed, effect, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CatalogueListing, GalleryReview, PublicGalleryPage } from '../../core/api/catalogue.api';
import { Paged, ResolvedText } from '../../core/api/common.api';
import { LookupsService } from '../../core/api/lookups.service';
import { ShortlistService } from '../../core/api/shortlist.service';
import { snapshotProblem } from '../../core/http/problem';
import { injectResponseStatus } from '../../core/http/server-context';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/en';
import { idFromSlug, slugFor } from '../../core/routing/slug';
import { SeoService } from '../../core/seo/seo.service';
import { CarCardComponent } from '../../shared/car-card/car-card.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { MapComponent } from '../../shared/map/map.component';
import { OfficeTextComponent } from '../../shared/office-text/office-text.component';
import { OpeningHoursComponent } from '../../shared/office-text/opening-hours.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { httpData } from '../../core/http/http-data';

/** Cars shown on an office's page before "view all in the search". Layout, not a business rule. */
const OFFICE_CARS = 12;

interface Section {
  readonly id: string;
  readonly title: TranslationKey;
  readonly text: ResolvedText;
}

/**
 * A rental office's own page. What is mandatory — who it is, where, its hours, whether and at what
 * price it delivers, its rating — is always there. What the office wrote is shown section by section,
 * and ONLY the sections the server returns: an office that hid its insurance note, or never wrote one,
 * gets no empty "Insurance" heading. The server does not say which of the two it was, and neither does
 * this page.
 */
@Component({
  selector: 'kh-dealer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent, CarCardComponent, OfficeTextComponent, OpeningHoursComponent, StatePanelComponent, MapComponent],
  templateUrl: './dealer.component.html',
})
export class DealerComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  private readonly lookups = inject(LookupsService);
  private readonly router = inject(Router);
  protected readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  readonly slug = input<string>('');
  protected readonly dealerId = computed(() => idFromSlug(this.slug()));

  protected readonly office = httpData<PublicGalleryPage>(() => {
    const id = this.dealerId();
    return id ? `/api/v1/galleries/${id}` : undefined;
  });
  protected readonly cars = httpData<Paged<CatalogueListing>>(() => {
    const id = this.dealerId();
    return id ? { url: '/api/v1/vehicles', params: { dealerId: id, page: 1, pageSize: OFFICE_CARS } } : undefined;
  });
  protected readonly reviews = httpData<Paged<GalleryReview>>(() => {
    const id = this.dealerId();
    return id && this.isBrowser ? { url: `/api/v1/galleries/${id}/reviews`, params: { page: 1, pageSize: 10 } } : undefined;
  });

  protected readonly problem = computed(() => (this.office.error() ? snapshotProblem(this.office.error()) : null));
  protected readonly notFound = computed(() => !this.dealerId() || this.problem()?.status === 404);
  protected readonly carsProblem = computed(() => (this.cars.error() ? snapshotProblem(this.cars.error()) : null));
  protected readonly city = computed(() => this.lookups.cityName(this.office.value()?.cityId));

  /** The office's own sections, in a fixed order, only those it shows. */
  protected readonly sections = computed<Section[]>(() => {
    const office = this.office.value();
    if (!office) return [];
    const s = office.sections;
    const all: (Section | null)[] = [
      s.about ? { id: 'about', title: 'office.about', text: s.about } : null,
      s.rentalConditions ? { id: 'terms', title: 'office.rentalConditions', text: s.rentalConditions } : null,
      s.insurance ? { id: 'insurance', title: 'office.insurance', text: s.insurance } : null,
      s.pickupInstructions ? { id: 'pickup', title: 'office.pickupInstructions', text: s.pickupInstructions } : null,
      office.delivery.isEnabled && s.deliveryNotes ? { id: 'delivery-notes', title: 'office.delivery', text: s.deliveryNotes } : null,
      s.customerNotes ? { id: 'notes', title: 'office.notes', text: s.customerNotes } : null,
    ];
    return all.filter((section): section is Section => section !== null);
  });

  protected readonly mapsUrl = computed(() => {
    const office = this.office.value();
    return office ? `https://www.openstreetmap.org/?mlat=${office.latitude}&mlon=${office.longitude}#map=16/${office.latitude}/${office.longitude}` : null;
  });

  protected readonly skeletons = Array.from({ length: 3 }, (_, index) => index);

  constructor() {
    const setStatus = injectResponseStatus();
    const seo = inject(SeoService);

    effect(() => {
      if (this.notFound()) {
        setStatus(404);
        seo.set({ title: this.i18n.t('seo.notFound.title'), noindex: true });
        return;
      }
      if (this.problem()) setStatus(503);
      const office = this.office.value();
      if (!office) return;

      const canonical = slugFor(office.dealerId, office.businessName);
      if (this.isBrowser && this.slug().toLowerCase() !== canonical) {
        void this.router.navigate(this.i18n.link('dealers', canonical), { replaceUrl: true });
      }

      seo.set({
        title: this.i18n.t('seo.office.title', { name: office.businessName }),
        description: this.i18n.t('seo.office.description', { name: office.businessName }),
        path: `dealers/${canonical}`,
        image: office.coverUrl ?? office.logoUrl,
        type: 'profile',
        structuredData: [
          {
            '@context': 'https://schema.org',
            '@type': 'AutoRental',
            name: office.businessName,
            ...(office.logoUrl ? { logo: seo.absoluteAsset(office.logoUrl) } : {}),
            ...(office.coverUrl ? { image: seo.absoluteAsset(office.coverUrl) } : {}),
            geo: { '@type': 'GeoCoordinates', latitude: office.latitude, longitude: office.longitude },
            ...(office.address
              ? { address: { '@type': 'PostalAddress', streetAddress: [office.address.street, office.address.area].filter(Boolean).join(', '), addressCountry: 'JO' } }
              : {}),
            openingHoursSpecification: office.operatingHours
              .filter((day) => !day.isClosed && day.opens && day.closes)
              .map((day) => ({ '@type': 'OpeningHoursSpecification', dayOfWeek: day.day, opens: day.opens!.slice(0, 5), closes: day.closes!.slice(0, 5) })),
            ...(office.averageRating !== null && office.reviewCount > 0
              ? { aggregateRating: { '@type': 'AggregateRating', ratingValue: office.averageRating, reviewCount: office.reviewCount, bestRating: 5, worstRating: 1 } }
              : {}),
          },
        ],
      });
    });

    const shortlist = inject(ShortlistService);
    effect(() => void shortlist.track(this.cars.value()?.items.map((car) => car.vehicleId) ?? []));
  }
}
