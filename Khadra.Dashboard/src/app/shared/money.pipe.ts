import { Pipe, PipeTransform, inject } from '@angular/core';
import { FormatService } from '../core/i18n/format.service';

/** The shape every money figure arrives in from the API. */
export interface MoneyValue {
  readonly amount: number | null | undefined;
  readonly currency?: string | null;
}

/**
 * An amount at the currency's own scale, for use straight from a template.
 *
 * It exists because two dozen templates interpolated `{{ x.amount }} {{ x.currency }}` raw. That
 * prints a 110.000 JOD booking as "110 JOD" — the dinar is divided into a thousand fils, so the
 * three decimals are not decoration — while the customer app, which formats properly, showed
 * "JOD 110.000" for the very same booking. Two surfaces disagreeing about a figure is worse than
 * either format alone, and a reader cannot tell which one to trust.
 *
 * A pipe rather than a helper on each component, because the fix had to reach every one of those
 * sites and a per-component method would have left the next template free to interpolate raw again.
 *
 * ## Two forms
 *
 * - `{{ value | money }}` — "110.000 JOD", the amount and its code as one isolated run.
 * - `{{ value | money: 'amount' }}` — "110.000" alone, for the layouts that place the code
 *   themselves (a `<small>` beside the figure, or a `min–max` range sharing one code).
 *
 * `impure` because the scale and the locale both arrive after the first render: the scale from
 * `/api/v1/app-config` at startup, the locale whenever the language switch is used. A pure pipe
 * would cache the first answer and keep printing English digits at the wrong precision.
 */
@Pipe({ name: 'money', pure: false })
export class MoneyPipe implements PipeTransform {
  private readonly formats = inject(FormatService);

  transform(value: MoneyValue | null | undefined, form: 'full' | 'amount' = 'full'): string {
    if (!value) return '—';
    return this.formats.money(value.amount, form === 'amount' ? null : value.currency);
  }
}
