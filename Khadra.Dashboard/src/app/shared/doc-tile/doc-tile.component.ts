import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DocumentTile, toneClass } from '../../core/models/console.models';
import { IconComponent } from '../icon/icon.component';

/**
 * A private document (dealer licence, customer ID, dispute evidence).
 *
 * The file itself is never inlined: these are access-controlled objects served
 * through short-lived signed URLs. The tile shows what the caller put in `file` — the document's
 * format on the dealer review screen, the stored name on dispute evidence — and a button that
 * requests the link. Never the URL itself, which is a credential.
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
