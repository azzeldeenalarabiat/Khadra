import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DocumentBodyView } from './invoice-presentation';

/**
 * An issued financial document, exactly as it was issued (payments Phase 5b): its title, its headline
 * figure, its sections in their stored order, its time note and its notice. Every word and figure is the
 * stored document's own, already chosen in the page's language by `documentBody`; this adds none.
 */
@Component({
  selector: 'kh-invoice-content',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './invoice-content.component.html',
})
export class InvoiceContentComponent {
  readonly body = input.required<DocumentBodyView>();
}
