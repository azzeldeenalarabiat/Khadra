import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Employee } from '../../core/models/dealer-console.api';
import { Tone } from '../../core/models/console.models';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { IconComponent } from '../../shared/icon/icon.component';

/**
 * Staff (spec 4.2, design `isEmployees`).
 *
 * An employee is invited by email and sets their own password from the link; the owner never sees
 * or types a password for them. Deactivating signs them out everywhere at once. The only permission
 * beyond "can act on bookings" is report access, and it is a switch the owner flips per person.
 *
 * Owner-only, and the API enforces that; an employee opening this page reads the list and nothing
 * else. Phone is required by the API (it is how a dealer reaches staff about a handover), which the
 * design left optional -- recorded as a deviation.
 */
@Component({
  selector: 'kh-dealer-employees',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-employees.component.html',
  imports: [IconComponent],
})
export class DealerEmployeesComponent {
  private readonly service = inject(DealerConsoleService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly resource = this.service.employees;
  protected readonly me = this.service.me;
  protected readonly busy = signal<string | null>(null);

  protected readonly employees = computed(() => this.resource.value() ?? []);
  protected readonly isOwner = computed(() => !!this.me.value()?.isOwner);
  protected readonly canInvite = computed(() => this.isOwner() && !!this.me.value()?.canTrade);

  protected readonly counts = computed(() => {
    const all = this.employees();
    return {
      active: all.filter((e) => e.status === 'Active').length,
      invited: all.filter((e) => e.status === 'Invited').length,
      deactivated: all.filter((e) => e.status === 'Deactivated').length,
    };
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error() as
      { status?: number; error?: { code?: string } } | undefined;
    if (!error) return null;
    if (error.status === 403) return 'Only the dealer owner can manage staff.';
    return 'Your staff list could not be loaded. Nothing has been changed.';
  });

  protected tone(e: Employee): Tone {
    return e.status === 'Active' ? 'ok' : e.status === 'Invited' ? 'warn' : 'dim';
  }

  protected initials(name: string): string {
    return name
      .split(' ')
      .slice(0, 2)
      .map((part) => part[0] ?? '')
      .join('');
  }

  protected when(iso: string | null): string {
    if (!iso) return 'Never';
    return new Date(iso).toLocaleString('en-GB', {
      day: '2-digit',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  protected invite(): void {
    this.ui.openAction(
      {
        icon: 'user-plus',
        tone: 'accent',
        title: 'Invite a staff member',
        body: 'They get an email with a link to set their own password. The link works for a limited time; you can resend it from this page.',
        confirm: 'Send invitation',
        fields: [
          { label: 'Full name', type: 'text', placeholder: 'e.g. Ahmad Zaid' },
          { label: 'Email', type: 'text', placeholder: 'name@example.jo' },
          {
            label: 'Phone',
            type: 'text',
            placeholder: '07XXXXXXXX',
            hint: 'Required. How you reach them about a handover.',
          },
          {
            label: 'Report access',
            type: 'select',
            options: ['No', 'Yes'],
            value: 'No',
            hint: 'Whether they can see revenue and the reports page. Bookings are always theirs to handle.',
          },
        ],
        result: { title: 'Invitation sent', body: 'They will find the link in their inbox.' },
      },
      async (values) => {
        const fullName = (values['Full name'] ?? '').trim();
        const email = (values['Email'] ?? '').trim();
        const phone = (values['Phone'] ?? '').trim();
        if (!fullName || !email || !phone) throw invalid('Name, email and phone are all required.');
        await this.service.invite({
          fullName,
          email,
          phone,
          canViewReports: values['Report access'] === 'Yes',
        });
        this.resource.reload();
        this.service.refreshMe();
      },
      { title: 'Invitation sent', body: 'They will find the link in their inbox.' },
    );
  }

  protected async resend(e: Employee): Promise<void> {
    await this.run(
      e.employeeId,
      () => this.service.resendInvitation(e.employeeId),
      'Invitation resent',
      `A fresh link is on its way to ${e.email}.`,
    );
  }

  protected async toggleReports(e: Employee): Promise<void> {
    const grant = !e.canViewReports;
    await this.run(
      e.employeeId,
      () => this.service.setReportAccess(e.employeeId, grant),
      grant ? 'Report access granted' : 'Report access removed',
      grant
        ? `${e.fullName} can now see revenue and reports.`
        : `${e.fullName} can still handle bookings; reports are hidden.`,
    );
  }

  protected deactivate(e: Employee): void {
    this.ui.openAction(
      {
        icon: 'user-minus',
        tone: 'bad',
        danger: true,
        title: `Deactivate ${e.fullName}?`,
        body: 'They are signed out everywhere immediately and can no longer open the console or act on bookings. Everything they did stays on record under their name. You can reactivate them later.',
        confirm: 'Deactivate',
        result: {
          title: 'Staff member deactivated',
          body: `${e.fullName} no longer has access.`,
          tone: 'warn',
        },
      },
      async () => {
        await this.service.deactivate(e.employeeId);
        this.resource.reload();
        this.service.refreshMe();
      },
      {
        title: 'Staff member deactivated',
        body: `${e.fullName} no longer has access.`,
        tone: 'warn',
      },
    );
  }

  protected async reactivate(e: Employee): Promise<void> {
    await this.run(
      e.employeeId,
      () => this.service.reactivate(e.employeeId),
      'Staff member reactivated',
      `${e.fullName} can sign in again with their existing password.`,
    );
  }

  private async run(
    id: string,
    action: () => Promise<unknown>,
    title: string,
    body: string,
  ): Promise<void> {
    if (this.busy()) return;
    this.busy.set(id);
    try {
      await action();
      this.resource.reload();
      this.service.refreshMe();
      this.ui.showToast(title, body);
    } catch (error) {
      const p = error as { error?: { title?: string } };
      this.ui.showToast(
        'That did not go through',
        p.error?.title ?? 'The service did not respond.',
        'bad',
      );
    } finally {
      this.busy.set(null);
    }
  }
}

/** Shaped like a ProblemDetails failure so the dialog shows it the same way it shows a server one. */
function invalid(title: string): { error: { title: string } } {
  return { error: { title } };
}
