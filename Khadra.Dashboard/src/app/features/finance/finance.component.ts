import { ChangeDetectionStrategy, Component } from '@angular/core';
import { COMMISSION_ROWS, FINANCE_KPIS } from '../../core/data/details.data';
import { toneClass } from '../../core/models/console.models';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * Platform revenue: gross value, the commission the platform keeps, and what is
 * owed to dealers. Every figure is JOD, stated in the header rather than repeated
 * on each cell.
 */
@Component({
  selector: 'kh-finance',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './finance.component.html',
  imports: [IconComponent],
})
export class FinanceComponent {
  protected readonly kpis = FINANCE_KPIS;
  protected readonly commission = COMMISSION_ROWS;
  protected readonly ranges = ['Today', 'This week', 'This month', 'Custom range'];
  protected readonly toneClass = toneClass;
}
