import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { CatalogueListing } from '../../core/api/catalogue.api';
import { ShortlistService } from '../../core/api/shortlist.service';
import { snapshotProblem } from '../../core/http/problem';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SeoService } from '../../core/seo/seo.service';
import { CarCardComponent } from '../../shared/car-card/car-card.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { httpData } from '../../core/http/http-data';

interface SavedVehicle {
  readonly vehicleId: string;
  readonly savedAt: string;
  readonly identity: { readonly make: string; readonly model: string; readonly year: number; readonly galleryName: string } | null;
  readonly listing: CatalogueListing | null;
  readonly isStillListed: boolean;
}

/**
 * The account's saved cars — the same list the app shows. A car that can no longer be booked keeps its
 * row, named, with its office, as "currently unavailable" and nothing more: never removed on its own,
 * never with a reason (owner, 2026-09-11). Only the customer removes it.
 */
@Component({
  selector: 'kh-saved',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, CarCardComponent, IconComponent, StatePanelComponent],
  templateUrl: './saved.component.html',
})
export class SavedComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  private readonly http = inject(HttpClient);
  private readonly shortlist = inject(ShortlistService);

  protected readonly saved = httpData<SavedVehicle[]>(() => '/api/v1/customers/me/shortlist');
  protected readonly problem = computed(() => (this.saved.error() ? snapshotProblem(this.saved.error()) : null));
  protected readonly removing = signal<string | null>(null);
  /** Rows the customer just removed here, or with a heart on this page. */
  private readonly gone = signal<ReadonlySet<string>>(new Set());
  protected readonly rows = computed(() =>
    (this.saved.value() ?? []).filter((row) => !this.gone().has(row.vehicleId) && (!row.listing || this.shortlist.savedIds().has(row.vehicleId) || !this.tracked().has(row.vehicleId))),
  );
  private readonly tracked = signal<ReadonlySet<string>>(new Set());

  constructor() {
    inject(SeoService).set({ title: this.i18n.t('seo.saved.title'), noindex: true });
    // Everything on this page is saved: tell the hearts so, without asking the server again.
    effect(() => {
      const rows = this.saved.value() ?? [];
      for (const row of rows) this.shortlist.setSaved(row.vehicleId, true);
      this.tracked.set(new Set(rows.map((row) => row.vehicleId)));
    });
  }

  protected async remove(row: SavedVehicle): Promise<void> {
    if (this.removing()) return;
    this.removing.set(row.vehicleId);
    try {
      await firstValueFrom(this.http.delete(`/api/v1/customers/me/shortlist/${row.vehicleId}`));
      this.shortlist.setSaved(row.vehicleId, false);
      this.gone.update((current) => new Set([...current, row.vehicleId]));
    } catch {
      this.saved.reload();
    } finally {
      this.removing.set(null);
    }
  }
}
