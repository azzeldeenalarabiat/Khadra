import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DocumentTile, toneClass } from '../../core/models/console.models';
import { IconComponent } from '../icon/icon.component';

/**
 * A private document (dealer licence, customer ID, dispute evidence).
 *
 * The file itself is never inlined: these are access-controlled objects served
 * through short-lived signed URLs, and every view is logged. The tile shows only
 * the filename and a button that would request one.
 */
@Component({
  selector: 'kh-doc-tile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './doc-tile.component.html',
  imports: [IconComponent],
})
export class DocTileComponent {
  readonly doc = input.required<DocumentTile>();
  /** Evidence tiles in the dispute screen show no preview button. */
  readonly showPreview = input<boolean>(true);

  protected readonly toneClass = toneClass;
}
