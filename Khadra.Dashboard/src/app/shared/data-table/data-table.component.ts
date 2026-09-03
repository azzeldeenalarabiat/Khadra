import { NgClass } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { Cell, RowAction, TableRow, toneClass } from '../../core/models/console.models';

/**
 * The console's one table. Every list screen and every profile tab renders
 * through it, which is why cells are data rather than markup: a new table is a
 * new array, not a new component.
 */
@Component({
  selector: 'kh-data-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './data-table.component.html',
  imports: [NgClass],
})
export class DataTableComponent {
  private readonly router = inject(Router);
  private readonly ui = inject(ConsoleUiService);

  readonly columns = input.required<readonly string[]>();
  readonly rows = input.required<readonly TableRow[]>();
  /** Below this width the table scrolls sideways rather than crushing columns. */
  readonly minWidth = input<string>('1040px');

  protected readonly toneClass = toneClass;

  protected openRow(row: TableRow): void {
    if (row.link) void this.router.navigate(row.link);
  }

  /** Row actions must not also trigger the row's own navigation. */
  protected runAction(event: Event, action: RowAction): void {
    event.stopPropagation();
    this.ui.run(action.action);
  }

  protected cellClasses(cell: Cell): Record<string, boolean> {
    const text = cell.kind === 'text' ? cell : null;
    return {
      td: true,
      'td-num': text?.align === 'right',
      'td-mono': text?.variant === 'mono',
      'td-muted': text?.variant === 'muted',
      'td-dim': text?.variant === 'dim',
      'td-accent': text?.variant === 'accent',
      'td-clip-280': text?.variant === 'clip-280',
      'td-clip-320': text?.variant === 'clip-320',
    };
  }

  protected initials(value: string): string {
    return value
      .split(' ')
      .slice(0, 2)
      .map((part) => part[0] ?? '')
      .join('');
  }

  protected buttonClass(action: RowAction): string {
    return `btn btn-${action.style} btn-xs`;
  }
}
