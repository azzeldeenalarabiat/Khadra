import { NgClass } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { Cell, RowAction, TableRow, toneClass } from '../../core/models/console.models';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';

/**
 * The console's one table. Every list screen and every profile tab renders
 * through it, which is why cells are data rather than markup: a new table is a
 * new array, not a new component.
 *
 * A navigable row exposes its first cell as a real link, so the row is reachable
 * by keyboard and openable in a new tab. The whole-row click is a mouse
 * convenience layered on top of that, never the only way in.
 */
@Component({
  selector: 'kh-data-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './data-table.component.html',
  imports: [NgClass, RouterLink],
})
export class DataTableComponent {
  protected readonly t = inject(I18nService).t;
  private readonly formats = inject(FormatService);

  /**
   * A money cell, at the currency's own scale.
   *
   * The template used the plain `number` pipe, whose default is up to three decimals and no
   * minimum -- so a 110.000 JOD total printed as "JOD 110". This table is shared across the
   * console, so that one default was wrong on a good many screens at once.
   */
  protected money(cell: { amount?: number | null; currency?: string | null }): string {
    return this.formats.money(cell.amount, cell.currency);
  }
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

  /** Row actions and the row link must not both fire. */
  protected runAction(event: Event, action: RowAction): void {
    event.stopPropagation();
    if (action.disabledReason) return;
    this.ui.run(action.action);
  }

  protected stop(event: Event): void {
    event.stopPropagation();
  }

  protected cellClasses(cell: Cell): Record<string, boolean> {
    const text = cell.kind === 'text' ? cell : null;
    return {
      td: true,
      'td-num': text?.align === 'right' || cell.kind === 'money',
      'td-mono': text?.variant === 'mono',
      'td-muted': text?.variant === 'muted',
      'td-dim': text?.variant === 'dim',
      'td-accent': text?.variant === 'accent',
      'td-clip-280': text?.variant === 'clip-280',
      'td-clip-320': text?.variant === 'clip-320',
      'status-text': !!text?.tone,
      [toneClass(text?.tone ?? 'dim')]: !!text?.tone,
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
