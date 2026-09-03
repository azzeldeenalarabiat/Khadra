import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  isDevMode,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';
import { LISTS } from '../../core/data/lists.data';
import { ViewState } from '../../core/models/console.models';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { DataTableComponent } from '../../shared/data-table/data-table.component';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * One component for every list in the console.
 *
 * Each route supplies a `list` key in its route data; the config comes from
 * LISTS. The design also exposes a state switcher so loading, empty, error and
 * access-denied can be reviewed without having to reproduce them. It is kept,
 * because those states are part of the design, but only in development: it
 * forces a screen into a state the data does not support, which would be a
 * confusing control to hand a real administrator.
 */
@Component({
  selector: 'kh-list-screen',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './list-screen.component.html',
  imports: [DataTableComponent, IconComponent],
})
export class ListScreenComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly ui = inject(ConsoleUiService);

  private readonly listKey = toSignal(this.route.data.pipe(map((data) => data['list'] as string)), {
    initialValue: this.route.snapshot.data['list'] as string,
  });

  protected readonly config = computed(() => LISTS[this.listKey()]);
  protected readonly viewState = signal<ViewState>('data');

  protected readonly showStateSwitcher = isDevMode();
  protected readonly states: readonly ViewState[] = ['data', 'loading', 'empty', 'error', 'denied'];
  protected readonly skeletonColumns = ['22%', '12%', '14%', '16%', '12%', '14%'];
  protected readonly skeletonRows = [0, 1, 2, 3, 4, 5, 6];
  protected readonly pages = [1, 2, 3, 4];

  protected setState(state: ViewState): void {
    this.viewState.set(state);
  }

  protected label(state: ViewState): string {
    return state.charAt(0).toUpperCase() + state.slice(1);
  }

  protected run(action: string): void {
    this.ui.run(action);
  }

  protected goDashboard(): void {
    void this.router.navigateByUrl('/dashboard');
  }
}
