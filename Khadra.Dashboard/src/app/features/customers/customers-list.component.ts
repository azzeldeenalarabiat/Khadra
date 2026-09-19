import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Tone } from '../../core/models/console.models';
import { CustomerListItem } from '../../core/models/customers.api';
import { AdminCustomersService } from '../../core/services/admin-customers.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';
import { FormatService } from '../../core/i18n/format.service';
import { TranslationKey } from '../../core/i18n/en';

/** A filter chip and the count behind it, both answered by the server. */
type Chip = { readonly label: string; readonly key: 'all' | 'active' | 'suspended' | 'unverified' };

/** The chips as keys; worded in `chips` so a language switch re-words them. */
const CHIPS: readonly { readonly key: Chip['key']; readonly label: TranslationKey }[] = [
  { key: 'all', label: 'common.all' },
  { key: 'active', label: 'status.active' },
  { key: 'suspended', label: 'status.suspended' },
  { key: 'unverified', label: 'status.unverified' },
];

/**
 * The people who rent (spec 5).
 *
 * Every count on this screen — including the ones on the filter chips — is the database's, not a
 * tally of the page in front of you. A figure counted from twenty visible rows and printed beside a
 * total of two hundred is indistinguishable from the real thing to the person reading it.
 */
@Component({
  selector: 'kh-customers-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './customers-list.component.html',
  imports: [RouterLink, IconComponent],
})
export class CustomersListComponent {
  protected readonly t = inject(I18nService).t;
  private readonly i18n = inject(I18nService);
  protected readonly formats = inject(FormatService);
  private readonly service = inject(AdminCustomersService);

  protected readonly resource = this.service.list;
  protected readonly countsResource = this.service.counts;
  protected readonly search = this.service.search;

  private readonly loadedPage = loaded(this.resource);
  private readonly loadedCounts = loaded(this.countsResource);

  protected readonly chips = computed<readonly Chip[]>(() =>
    CHIPS.map((chip) => ({ key: chip.key, label: this.t(chip.label) })),
  );

  protected readonly active = computed<Chip['key']>(() => {
    if (this.service.unverifiedOnly()) return 'unverified';
    const status = this.service.status();
    if (status === 'Active') return 'active';
    if (status === 'Suspended') return 'suspended';
    return 'all';
  });

  /** The count behind a chip, or null until the server has answered. Never an invented zero. */
  protected count(key: Chip['key']): number | null {
    const counts = this.loadedCounts();
    if (!counts) return null;
    switch (key) {
      case 'all':
        return counts.total;
      case 'active':
        return counts.active;
      case 'suspended':
        return counts.suspended;
      case 'unverified':
        return counts.unverified;
    }
  }

  protected readonly rows = computed(() => this.loadedPage()?.items ?? []);
  protected readonly totalPages = computed(() => this.loadedPage()?.totalPages ?? 1);
  protected readonly page = computed(() => this.loadedPage()?.page ?? 1);

  protected readonly summary = computed(() => {
    const page = this.loadedPage();
    if (!page) return '';
    const from = page.totalCount === 0 ? 0 : (page.page - 1) * page.pageSize + 1;
    const to = Math.min(page.page * page.pageSize, page.totalCount);
    // One plural message on the total, so the noun agrees with it in Arabic as well as English.
    return this.t('customersList.showingRange', { from, to, count: page.totalCount });
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error() as { status?: number } | undefined;
    if (!error) return null;
    if (error.status === 403) return this.t('customersList.theCustomerListIs');
    return this.t('customersList.theCustomersCouldNot');
  });

  protected select(key: Chip['key']): void {
    this.service.status.set(key === 'active' ? 'Active' : key === 'suspended' ? 'Suspended' : null);
    this.service.unverifiedOnly.set(key === 'unverified');
    this.service.page.set(1);
  }

  protected setSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
    this.service.page.set(1);
  }

  protected goTo(page: number): void {
    if (page >= 1 && page <= this.totalPages()) this.service.page.set(page);
  }

  protected reload(): void {
    this.resource.reload();
  }

  protected tone(row: CustomerListItem): Tone {
    return row.status === 'Suspended' ? 'bad' : row.isEmailVerified ? 'ok' : 'warn';
  }

  /** A suspension is named first; otherwise whether the email has been verified, which is a flag. */
  protected statusLabel(row: CustomerListItem): string {
    if (row.status === 'Suspended') return this.i18n.statusLabel(row.status);
    return this.t(row.isEmailVerified ? 'status.verified' : 'status.unverified');
  }

  protected initials(name: string): string {
    return name
      .split(' ')
      .slice(0, 2)
      .map((part) => part[0] ?? '')
      .join('');
  }

  protected when(iso: string | null): string {
    // Never signed in is a fact worth stating; a date invented for it would not be.
    return iso ? this.formats.date(iso) : this.t('common.never');
  }
}
