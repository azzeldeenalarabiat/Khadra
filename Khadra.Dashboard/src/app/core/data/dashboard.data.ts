import { IconName } from '../../shared/icon/icon-paths';
import { Tone } from '../models/console.models';

/**
 * View-model shapes for the dashboard screen.
 *
 * This file used to hold the design's sample content. It no longer holds any: the dashboard reads
 * one endpoint per panel and `dashboard.presenter.ts` maps those responses into these shapes.
 * What is left is the contract between the presenter and the template — the KPI card and the work
 * queue row as the design draws them, with no colours or figures baked in.
 */

export interface KpiCard {
  readonly label: string;
  readonly icon: IconName;
  readonly main: string;
  readonly route: string;
  readonly subs: readonly { readonly k: string; readonly v: string; readonly tone?: Tone }[];
}

export interface QueueItem {
  /**
   * The API's own key for this item.
   *
   * The rows were tracked by title + entity, which collides for two disputes of the same age between
   * the same dealer and customer — and a colliding track key makes Angular reuse the wrong row.
   */
  readonly id: string;
  readonly severity: string;
  readonly tone: Tone;
  readonly title: string;
  readonly description: string;
  readonly entity: string;
  /** Rendered countdown, e.g. "13h over". Recomputed locally from the deadline the API sent. */
  readonly sla: string;
  /** How much of the SLA window has elapsed, as a percentage. */
  readonly percent: number;
  readonly action: string;
  readonly route: string;
}
