import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TimelineStep, toneClass } from '../../core/models/console.models';

/**
 * Vertical timeline used by the application, booking, payment and dispute
 * screens. A step marked `future` draws a hollow dot and a muted label, so what
 * has happened is visually separate from what is merely due.
 */
@Component({
  selector: 'kh-timeline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './timeline.component.html',
})
export class TimelineComponent {
  readonly steps = input.required<readonly TimelineStep[]>();

  protected readonly toneClass = toneClass;

  protected isLast(index: number): boolean {
    return index === this.steps().length - 1;
  }
}
