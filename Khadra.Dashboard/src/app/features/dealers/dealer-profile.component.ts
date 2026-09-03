import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  DEALER_DOCUMENTS,
  DEALER_PROFILE,
  DEALER_PROFILE_ROWS,
  DEALER_STATS,
  DEALER_TABS,
  DEALER_TAB_TABLES,
  DEALER_VERIFICATION,
} from '../../core/data/details.data';
import { toneClass } from '../../core/models/console.models';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { DataTableComponent } from '../../shared/data-table/data-table.component';
import { DocTileComponent } from '../../shared/doc-tile/doc-tile.component';
import { IconComponent } from '../../shared/icon/icon.component';

/** Approved dealer record, with one tab per facet of the business. */
@Component({
  selector: 'kh-dealer-profile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-profile.component.html',
  imports: [IconComponent, DocTileComponent, DataTableComponent],
})
export class DealerProfileComponent {
  private readonly ui = inject(ConsoleUiService);

  protected readonly dealer = DEALER_PROFILE;
  protected readonly rows = DEALER_PROFILE_ROWS;
  protected readonly verification = DEALER_VERIFICATION;
  protected readonly stats = DEALER_STATS;
  protected readonly documents = DEALER_DOCUMENTS;
  protected readonly tabs = DEALER_TABS;
  protected readonly toneClass = toneClass;

  protected readonly activeTab = signal<string>('Overview');
  protected readonly table = computed(() => DEALER_TAB_TABLES[this.activeTab()]);

  protected select(tab: string): void {
    this.activeTab.set(tab);
  }

  protected suspend(): void {
    this.ui.openModal('suspend-dealer');
  }
}
