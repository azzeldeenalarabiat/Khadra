import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ResolvedText } from '../../core/api/common.api';

/**
 * Something a rental office wrote, rendered in the language it was actually written in. The server
 * falls back to the other language when the office wrote only one, so the paragraph carries its own
 * `lang` and lets the browser set its direction from the text — an Arabic paragraph on an English
 * page still reads right to left.
 */
@Component({
  selector: 'kh-office-text',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './office-text.component.html',
})
export class OfficeTextComponent {
  readonly text = input.required<ResolvedText>();
}
