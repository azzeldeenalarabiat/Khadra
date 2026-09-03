import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * Requests a password-reset link.
 *
 * The screen deliberately says the same thing whether or not the address is on file. The API answers
 * 202 either way so that nobody can use this form to discover which emails have accounts, and the UI
 * would give that away again if it distinguished them.
 */
@Component({
  selector: 'kh-forgot-password',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './forgot-password.component.html',
  imports: [FormsModule, RouterLink, IconComponent],
})
export class ForgotPasswordComponent {
  private readonly http = inject(HttpClient);

  protected readonly email = signal('');
  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly problem = signal<string | null>(null);

  /** Narrows an input event to the value the control holds. */
  protected fieldValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async submit(): Promise<void> {
    const email = this.email().trim();
    if (this.busy() || !email) {
      this.problem.set(email ? null : 'Enter the email address on your account.');
      return;
    }

    this.busy.set(true);
    this.problem.set(null);

    try {
      const token = await firstValueFrom(
        this.http.get<{ requestToken: string }>('/bff/antiforgery'),
      );
      await firstValueFrom(
        this.http.post(
          '/api/v1/auth/forgot-password',
          { email },
          { headers: { 'X-XSRF-TOKEN': token.requestToken } },
        ),
      );
      this.sent.set(true);
    } catch {
      this.problem.set('The service did not respond. Try again shortly.');
    } finally {
      this.busy.set(false);
    }
  }
}
