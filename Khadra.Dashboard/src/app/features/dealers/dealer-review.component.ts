import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import {
  APPLICATION_TIMELINE,
  DEALER_APPLICATION,
  DEALER_BUSINESS_ROWS,
  DEALER_DOCUMENTS,
} from '../../core/data/details.data';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { DocTileComponent } from '../../shared/doc-tile/doc-tile.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { TimelineComponent } from '../../shared/timeline/timeline.component';

/**
 * Dealer application review: the Admin's licence check (spec 3.1).
 *
 * The three outcomes are approve, reject and request clarification; the last two
 * require a written reason, which is why all three go through a dialog.
 */
@Component({
  selector: 'kh-dealer-review',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-review.component.html',
  imports: [IconComponent, DocTileComponent, TimelineComponent],
})
export class DealerReviewComponent {
  private readonly ui = inject(ConsoleUiService);

  protected readonly application = DEALER_APPLICATION;
  protected readonly businessRows = DEALER_BUSINESS_ROWS;
  protected readonly documents = DEALER_DOCUMENTS;
  protected readonly timeline = APPLICATION_TIMELINE;

  protected open(id: string): void {
    this.ui.openModal(id);
  }
}
