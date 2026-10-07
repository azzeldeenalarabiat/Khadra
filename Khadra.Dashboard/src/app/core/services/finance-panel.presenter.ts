import { TranslationKey } from '../i18n/en';
import { MessageParams } from '../i18n/language';
import { FinanceSummary, PanelMoney } from '../models/dashboard.api';

type Translate = (key: TranslationKey, params?: MessageParams) => string;

/** How the panel prints a figure, and the month it covers. */
export interface FinancePanelFormat {
  money(value: PanelMoney): string;
  /** A calendar date (YYYY-MM-DD) as its month, never through a time zone. */
  calendarMonthYear(isoDate: string): string;
}

/** One figure on the panel, with the count behind it. */
export interface FinanceLine {
  readonly label: string;
  readonly value: string;
  readonly note: string | null;
  /** The part of the stock that is a subset of the lines above it: shown indented, never added. */
  readonly part?: boolean;
  readonly hi?: boolean;
}

export interface FinancePanel {
  readonly mode: string;
  /** Test money: the panel says so above its figures, as the consoles' banner does. */
  readonly sandbox: boolean;
  readonly period: string;
  readonly month: readonly FinanceLine[];
  readonly now: readonly FinanceLine[];
  /**
   * Capture incidents nobody has marked handled (Wave 4, B1): a count, set apart from the money owed back because
   * it is not part of it. Null when the API sent no count.
   */
  readonly incidents: FinanceLine | null;
  readonly otherCurrencies: readonly string[];
}

const MODES: Readonly<Record<string, TranslationKey>> = {
  None: 'paymentMode.none',
  Sandbox: 'paymentMode.sandbox',
  Live: 'paymentMode.live',
};

/**
 * "Money in motion" in words (payments Phase 4b). Every figure is the server's: the month's flows are
 * counted by their own event dates, the stock is what is owed back at the moment it was generated, and
 * the orphaned captures are a PART of that stock — shown under it, never added to it.
 */
export function financePanel(summary: FinanceSummary, t: Translate, format: FinancePanelFormat): FinancePanel {
  const month = summary.thisMonth;
  const now = summary.rightNow;
  return {
    mode: MODES[summary.paymentMode] ? t(MODES[summary.paymentMode]) : summary.paymentMode,
    sandbox: summary.paymentMode === 'Sandbox',
    period: t('adminDashboard.thisMonth', { month: format.calendarMonthYear(month.from) }),
    month: [
      {
        label: t('adminDashboard.appliedToBookings'),
        value: format.money(month.appliedToBookings),
        note: t('adminDashboard.paymentsCount', { count: month.paymentsApplied }),
        hi: true,
      },
      { label: t('adminDashboard.feesCharged'), value: format.money(month.processingFeesCharged), note: null },
      {
        label: t('adminDashboard.refundsSettled'),
        value: format.money(month.refundsSettled),
        note: t('adminDashboard.refundsCount', { count: month.refundsSettledCount }),
      },
    ],
    now: [
      {
        label: t('adminDashboard.refundsOnTheirWay'),
        value: format.money(now.refundsInProgress),
        note: t('adminDashboard.refundsCount', { count: now.refundsInProgressCount }),
      },
      {
        label: t('adminDashboard.refundsRefused'),
        value: format.money(now.refundsFailed),
        note: t('adminDashboard.refundsCount', { count: now.refundsFailedCount }),
        hi: now.refundsFailedCount > 0,
      },
      {
        label: t('adminDashboard.ofWhichOrphans'),
        value: format.money(now.orphanedCapturesOwed),
        note: t('adminDashboard.refundsCount', { count: now.orphanedCapturesOwedCount }),
        part: true,
      },
    ],
    incidents:
      typeof now.openCaptureIncidentsCount === 'number'
        ? {
            label: t('adminDashboard.captureIncidents'),
            value: t('adminDashboard.incidentsCount', { count: now.openCaptureIncidentsCount }),
            note: null,
            hi: now.openCaptureIncidentsCount > 0,
          }
        : null,
    otherCurrencies: summary.otherCurrencies.map((other) =>
      t('adminDashboard.otherCurrency', {
        currency: other.currency,
        settled: format.money(other.refundsSettledThisMonth),
        outstanding: format.money(other.refundsOutstanding),
      }),
    ),
  };
}
