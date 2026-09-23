import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { AccountUser } from '../../core/api/account.api';
import { AppConfigService } from '../../core/config/app-config.service';
import { ProblemSnapshot, snapshotProblem } from '../../core/http/problem';
import { problemText } from '../../core/http/problem-text';
import { FormatService } from '../../core/i18n/format.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SessionService } from '../../core/session/session.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { StatePanelComponent } from '../../shared/state/state-panel.component';
import { httpData } from '../../core/http/http-data';

/** Name and mobile number, as the rental office sees them on a booking. The email is not editable. */
@Component({
  selector: 'kh-profile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, IconComponent, StatePanelComponent],
  templateUrl: './profile.component.html',
})
export class ProfileComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly format = inject(FormatService);
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly appConfig = inject(AppConfigService);

  protected readonly me = httpData<AccountUser>(() => '/api/v1/auth/me');
  protected readonly loadProblem = computed(() => (this.me.error() ? snapshotProblem(this.me.error()) : null));

  protected readonly fullName = signal('');
  protected readonly phone = signal('');
  protected readonly busy = signal(false);
  protected readonly saved = signal(false);
  protected readonly problem = signal<ProblemSnapshot | null>(null);
  protected readonly message = computed(() => {
    const problem = this.problem();
    return problem ? problemText(problem, this.i18n.t.bind(this.i18n), this.i18n.language(), this.appConfig.config()) : null;
  });

  constructor() {
    effect(() => {
      const me = this.me.value();
      if (!me) return;
      this.fullName.set(me.fullName);
      this.phone.set(me.phone);
    });
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy() || !this.fullName().trim() || !this.phone().trim()) return;
    this.busy.set(true);
    this.saved.set(false);
    this.problem.set(null);
    try {
      const updated = await firstValueFrom(
        this.http.put<AccountUser>('/api/v1/customers/me/profile', { fullName: this.fullName().trim(), phone: this.phone().trim() }),
      );
      this.me.set(updated);
      this.session.updateUser({ fullName: updated.fullName, phone: updated.phone });
      this.saved.set(true);
    } catch (error) {
      this.problem.set(snapshotProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
