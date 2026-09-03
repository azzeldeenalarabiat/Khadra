import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  CUSTOMER_DOCUMENTS,
  CUSTOMER_PROFILE,
  CUSTOMER_ROWS,
  CUSTOMER_STATS,
  CUSTOMER_TABS,
  CUSTOMER_TAB_TABLES,
} from '../../core/data/details.data';
import { toneClass } from '../../core/models/console.models';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { DataTableComponent } from '../../shared/data-table/data-table.component';
import { DocTileComponent } from '../../shared/doc-tile/doc-tile.component';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * Customer record. The verification tab holds identity documents, which are
 * restricted and logged on every view (spec 7).
 */
@Component({
  selector: 'kh-customer-profile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './customer-profile.component.html',
  imports: [IconComponent, DocTileComponent, DataTableComponent],
})
export class CustomerProfileComponent {
  private readonly ui = inject(ConsoleUiService);

  protected readonly customer = CUSTOMER_PROFILE;
  protected readonly rows = CUSTOMER_ROWS;
  protected readonly stats = CUSTOMER_STATS;
  protected readonly documents = CUSTOMER_DOCUMENTS;
  protected readonly tabs = CUSTOMER_TABS;
  protected readonly toneClass = toneClass;

  protected readonly activeTab = signal<string>('Overview');
  protected readonly table = computed(() => CUSTOMER_TAB_TABLES[this.activeTab()]);

  protected select(tab: string): void {
    this.activeTab.set(tab);
  }

  protected suspend(): void {
    this.ui.openModal('deactivate-account');
  }
}
