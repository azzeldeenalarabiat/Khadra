import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  ATTENTION_QUEUE,
  BOOKING_TREND,
  KPIS,
  MONEY_FLOW,
  RECENT_ACTIVITY,
} from '../../core/data/dashboard.data';
import { toneClass } from '../../core/models/console.models';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * Landing screen: platform figures, the work queue that drives the 48-hour SLA,
 * and a short activity feed.
 */
@Component({
  selector: 'kh-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.component.html',
  imports: [RouterLink, IconComponent],
})
export class DashboardComponent {
  protected readonly kpis = KPIS;
  protected readonly queue = ATTENTION_QUEUE;
  protected readonly trend = BOOKING_TREND;
  protected readonly money = MONEY_FLOW;
  protected readonly activity = RECENT_ACTIVITY;
  protected readonly toneClass = toneClass;
}
