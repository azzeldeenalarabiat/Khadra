import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  DISPUTE_CARDS,
  DISPUTE_DETAIL,
  DISPUTE_EVIDENCE,
  DISPUTE_TIMELINE,
  PAST_RESOLUTIONS,
  RESOLUTION_OPTIONS,
} from '../../core/data/details.data';
import { toneClass } from '../../core/models/console.models';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { TimelineComponent } from '../../shared/timeline/timeline.component';

/**
 * Dispute resolution (spec 3.3). The Admin's decision, note and timestamp are
 * shown to both parties and written to the audit log, so the panel states that
 * plainly next to the confirm button.
 */
@Component({
  selector: 'kh-dispute-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dispute-detail.component.html',
  imports: [IconComponent, TimelineComponent],
})
export class DisputeDetailComponent {
  private readonly ui = inject(ConsoleUiService);

  protected readonly dispute = DISPUTE_DETAIL;
  protected readonly cards = DISPUTE_CARDS;
  protected readonly evidence = DISPUTE_EVIDENCE;
  protected readonly timeline = DISPUTE_TIMELINE;
  protected readonly resolutions = RESOLUTION_OPTIONS;
  protected readonly pastResolutions = PAST_RESOLUTIONS;
  protected readonly toneClass = toneClass;

  protected readonly selected = signal<string>('Partial penalty');

  protected select(label: string): void {
    this.selected.set(label);
  }

  protected resolve(): void {
    this.ui.openModal('resolve-dispute');
  }

  protected requestInfo(): void {
    this.ui.openModal('more-info');
  }
}
