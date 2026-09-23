import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PublicGalleryCard } from '../../core/api/catalogue.api';
import { Paged } from '../../core/api/common.api';
import { LookupsService } from '../../core/api/lookups.service';
import { snapshotProblem } from '../../core/http/problem';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { OfficeCardComponent } from '../../shared/office-card/office-card.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';

const PAGE_SIZE = 18;
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** The directory of rental offices, optionally in one city. The city and page are in the URL. */
@Component({
  selector: 'kh-dealers',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, OfficeCardComponent, StatePanelComponent, IconComponent],
  templateUrl: './dealers.component.html',
})
export class DealersComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly lookups = inject(LookupsService);
  private readonly router = inject(Router);

  private readonly params = toSignal(inject(ActivatedRoute).queryParams, { initialValue: {} as Record<string, string> });
  protected readonly city = computed(() => {
    const value = this.params()['city'];
    return typeof value === 'string' && GUID.test(value) ? value.toLowerCase() : null;
  });
  protected readonly page = computed(() => {
    const value = Number(this.params()['page']);
    return Number.isInteger(value) && value > 1 ? value : 1;
  });

  protected readonly offices = httpResource<Paged<PublicGalleryCard>>(() => ({
    url: '/api/v1/galleries',
    params: { page: this.page(), pageSize: PAGE_SIZE, ...(this.city() ? { cityId: this.city()! } : {}) },
  }));
  protected readonly problem = computed(() => (this.offices.error() ? snapshotProblem(this.offices.error()) : null));
  protected readonly skeletons = Array.from({ length: 6 }, (_, index) => index);

  constructor() {
    const seo = inject(SeoService);
    effect(() => {
      const city = this.city();
      seo.set({
        title: this.i18n.t('seo.offices.title'),
        description: this.i18n.t('seo.offices.description'),
        path: city ? `dealers?city=${city}` : 'dealers',
        noindex: this.page() > 1,
      });
    });
  }

  protected setCity(value: string): void {
    void this.router.navigate([], { queryParams: value ? { city: value } : {} });
  }

  protected pageParams(page: number): Record<string, string> {
    return { ...(this.city() ? { city: this.city()! } : {}), ...(page > 1 ? { page: String(page) } : {}) };
  }
}
