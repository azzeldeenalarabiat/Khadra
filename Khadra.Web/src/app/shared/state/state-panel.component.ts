import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { IconComponent } from '../icon/icon.component';
import { IconName } from '../icon/icon-paths';
import { I18nService } from '../../core/i18n/i18n.service';
import { ProblemSnapshot } from '../../core/http/problem';

/**
 * The one way a page says "nothing here", "something failed" or "the service is asleep".
 *
 * Every important page has these three states, and they read the same everywhere: an icon, a
 * sentence, and — when there is something the visitor can do — one action. An error that carries a
 * trace id shows it, so a customer who contacts support can quote it.
 */
@Component({
  selector: 'kh-state-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent],
  templateUrl: './state-panel.component.html',
})
export class StatePanelComponent {
  protected readonly i18n = inject(I18nService);

  readonly kind = input<'empty' | 'error'>('empty');
  readonly icon = input<IconName | null>(null);
  readonly title = input<string>('');
  readonly text = input<string>('');
  readonly problem = input<ProblemSnapshot | null>(null);
  readonly actionLabel = input<string | null>(null);
  readonly compact = input(false);
  readonly action = output<void>();

  /** An API that never answered reads as "asleep", not as a fault: on Render it usually is. */
  protected readonly unreachable = computed(() => {
    const problem = this.problem();
    return problem !== null && (problem.status === 0 || problem.status === 502 || problem.status === 503 || problem.status === 504);
  });

  protected readonly shownIcon = computed<IconName>(
    () => this.icon() ?? (this.kind() === 'error' ? (this.unreachable() ? 'cloud-slash' : 'warning-circle') : 'tray'),
  );

  protected readonly shownTitle = computed(() => {
    if (this.title()) return this.title();
    if (this.kind() !== 'error') return '';
    return this.i18n.t(this.unreachable() ? 'state.unavailable.title' : 'state.error.title');
  });

  protected readonly shownText = computed(() => {
    if (this.text()) return this.text();
    if (this.kind() !== 'error') return '';
    return this.i18n.t(this.unreachable() ? 'state.unavailable.text' : 'state.error.text');
  });

  protected readonly shownAction = computed(
    () => this.actionLabel() ?? (this.kind() === 'error' ? this.i18n.t('common.retry') : null),
  );
}
