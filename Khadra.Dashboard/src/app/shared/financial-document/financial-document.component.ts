import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DocumentBodyView } from '../../features/payments/financial-documents.presenter';

/**
 * An issued financial document, exactly as it was issued (payments Phase 5b): its title, its headline
 * figure, its sections in their stored order, its time note and its notice. Every word and figure is the
 * stored document's own, chosen in the console's language by `documentBody`; this adds none.
 *
 * A literal the document registered keeps its own direction: a Latin number, reference, plate or phone
 * runs left to right, and a name written in Arabic takes its own direction (`<bdi>`), so neither is
 * reordered by the text around it.
 */
@Component({
  selector: 'kh-financial-document',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './financial-document.component.html',
})
export class FinancialDocumentComponent {
  readonly body = input.required<DocumentBodyView>();
}
