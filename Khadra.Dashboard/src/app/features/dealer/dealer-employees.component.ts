import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Employee } from '../../core/models/dealer-console.api';
import { Tone } from '../../core/models/console.models';
import { DealerConsoleService } from '../../core/services/dealer-console.service';
import { ConsoleUiService } from '../../core/services/console-ui.service';
import { loaded } from '../../core/services/loaded';
import { IconComponent } from '../../shared/icon/icon.component';
import { I18nService } from '../../core/i18n/i18n.service';

/**
 * Staff (spec 4.2, design `isEmployees`).
 *
 * An employee is invited by email and sets their own password from the link; the owner never sees
 * or types a password for them. Deactivating signs them out everywhere at once. The only permission
 * beyond "can act on bookings" is report access, and it is a switch the owner flips per person.
 *
 * Owner-only, and not partly: `DealerEmployeesController` carries `ApprovedDealer` on the CLASS, so
 * an employee is refused this list even to read it, and so is an owner whose dealership cannot yet
 * trade. Both are told which of those two they are, before the request rather than after it — the
 * list is not asked for at all without `canManageStaff`, because a role refusal comes back as a
 * bodiless 403 that no screen can turn into a sentence.
 *
 * Phone is required by the API (it is how a dealer reaches staff about a handover), which the design
 * left optional -- recorded as a deviation.
 */
@Component({
  selector: 'kh-dealer-employees',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dealer-employees.component.html',
  imports: [IconComponent, RouterLink],
})
export class DealerEmployeesComponent {
  protected readonly t = inject(I18nService).t;
  // Server enum names, in the reader's language. Shared rather than per-component: the same enum
  // shows on half a dozen screens, and a copy each is a copy each to forget a new member in.
  protected readonly statusLabel = inject(I18nService).statusLabel;
  private readonly service = inject(DealerConsoleService);
  private readonly ui = inject(ConsoleUiService);

  protected readonly resource = this.service.employees;
  /** Guarded: `value()` throws in the error state, so nothing reads the resource directly. */
  private readonly data = loaded(this.resource);
  protected readonly me = this.service.me;
  private readonly dealer = loaded(this.me);
  protected readonly busy = signal<string | null>(null);

  protected readonly employees = computed(() => this.data() ?? []);
  protected readonly isOwner = computed(() => !!this.dealer()?.isOwner);

  /**
   * Whether the list is this person's to read at all. `null` until `GET /dealers/me` answers, so
   * nothing claims anything in the window before the console knows who is asking.
   */
  protected readonly canManage = computed(() => this.service.permissions()?.canManageStaff ?? null);
  protected readonly canInvite = computed(() => this.canManage() === true);

  protected readonly counts = computed(() => {
    const all = this.employees();
    return {
      active: all.filter((e) => e.status === 'Active').length,
      invited: all.filter((e) => e.status === 'Invited').length,
      deactivated: all.filter((e) => e.status === 'Deactivated').length,
    };
  });

  /**
   * Why the list is closed to the person reading, in their own terms.
   *
   * The same 403 used to answer both of them with "only the dealer owner can manage staff", which is
   * the one sentence that cannot be true of the owner it was shown to. Now the answer comes from
   * `me`: not the owner, or the owner of a dealership that may not trade yet.
   */
  protected readonly denial = computed<{ title: string; body: string } | null>(() => {
    if (this.canManage() !== false) return null;

    if (!this.isOwner()) {
      return {
        title: this.t('dealerEmployees.staffIsTheOwners'),
        body: this.t('dealerEmployees.whoWorksHereWhat'),
      };
    }
    return {
      title: this.t('dealerEmployees.staffOpensOnceYour'),
      body: this.t('dealerEmployees.invitingPeopleGrantingReport'),
    };
  });

  protected readonly failure = computed(() => {
    const error = this.resource.error() as
      { status?: number; error?: { code?: string } } | undefined;
    if (!error) return null;
    // Reachable when standing changes under an open screen -- a suspension landing mid-session.
    if (error.status === 403) return this.t('dealerStaff.yourDealershipCanNo');
    return this.t('dealerStaff.yourStaffListCould');
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
        title: this.t('dealerEmployees.inviteAStaffMember'),
        body: this.t('dealerEmployees.theyGetAnEmail'),
        confirm: this.t('dealerEmployees.sendInvitation'),
        fields: [
          { name: 'fullName', label: this.t('employeeSettings.fullName'), type: 'text', placeholder: this.t('dealerEmployees.eGAhmadZaid') },
          { name: this.t('dealerStaff.email'), label: this.t('dealerSettings.email'), type: 'text', placeholder: 'name@example.jo' },
          {
            name: this.t('dealerStaff.phone'),
            label: this.t('customerProfile.phone'),
            type: 'text',
            placeholder: '07XXXXXXXX',
            hint: this.t('dealerEmployees.requiredHowYouReach'),
          },
          {
            name: 'reportAccess',
            label: this.t('dealerEmployees.reportAccess'),
            type: 'select',
            options: [
              { value: 'no', label: this.t('dealerStaff.no') },
              { value: 'yes', label: this.t('dealerStaff.yes') },
            ],
            value: 'no',
            hint: this.t('dealerEmployees.whetherTheyCanSee'),
          },
        ],
        result: { title: this.t('dealerEmployees.invitationSent'), body: this.t('dealerEmployees.theyWillFindThe') },
      },
      async (values) => {
        const fullName = (values['fullName'] ?? '').trim();
        const email = (values['email'] ?? '').trim();
        const phone = (values['phone'] ?? '').trim();
        if (!fullName || !email || !phone) throw invalid(this.t('dealerStaff.nameEmailAndPhone'));
        await this.service.invite({
          fullName,
          email,
          phone,
          canViewReports: values['reportAccess'] === 'yes',
        });
        this.resource.reload();
        this.service.refreshMe();
      },
      { title: this.t('dealerEmployees.invitationSent'), body: this.t('dealerEmployees.theyWillFindThe') },
    );
  }

  protected async resend(e: Employee): Promise<void> {
    await this.run(
      e.employeeId,
      () => this.service.resendInvitation(e.employeeId),
      this.t('dealerStaff.invitationResent'),
      `A fresh link is on its way to ${e.email}.`,
    );
  }

  protected async toggleReports(e: Employee): Promise<void> {
    const grant = !e.canViewReports;
    await this.run(
      e.employeeId,
      () => this.service.setReportAccess(e.employeeId, grant),
      grant ? this.t('dealerStaff.reportAccessGranted') : this.t('dealerStaff.reportAccessRemoved'),
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
        body: this.t('dealerEmployees.theyAreSignedOut'),
        confirm: 'Deactivate',
        result: {
          title: this.t('dealerEmployees.staffMemberDeactivated'),
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
        title: this.t('dealerEmployees.staffMemberDeactivated'),
        body: `${e.fullName} no longer has access.`,
        tone: 'warn',
      },
    );
  }

  protected async reactivate(e: Employee): Promise<void> {
    // Someone deactivated before they ever accepted their invitation has no password to sign in
    // with, and telling them to use it sends the owner looking for a problem that is not there.
    // `status` is on the record, so the sentence can say which of the two this is.
    const back =
      e.status === 'Invited'
        ? `${e.fullName} still needs to accept their invitation and set a password.`
        : `${e.fullName} can sign in again with their existing password.`;
    await this.run(
      e.employeeId,
      () => this.service.reactivate(e.employeeId),
      this.t('dealerStaff.staffMemberReactivated'),
      back,
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
        this.t('vehicleDetail.thatDidNotGo'),
        p.error?.title ?? this.t('vehicleDetail.theServiceDidNot'),
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
