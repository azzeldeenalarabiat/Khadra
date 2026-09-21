import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { I18nService } from '../../core/i18n/i18n.service';
import { PlatformConfigService } from '../../core/services/platform-config.service';
import { IconComponent } from '../icon/icon.component';

/**
 * Says, across the whole console, that this deployment takes no real money.
 *
 * A sandbox capture writes the same `Confirmed` status, the same row and the same dealer screen as a
 * real one. That is what makes it useful for testing and what makes it dangerous afterwards: within
 * a day nobody can tell which bookings had money behind them. The payment record carries a permanent
 * marker for whoever reads the database later; this is the half for whoever is reading the screen.
 *
 * **It renders nothing unless the API itself said `Sandbox`.** Not while the config is in flight,
 * not when the call failed, not on a value this build has never seen. A banner missed on a test host
 * is a nuisance; a banner shown over a real dealer's real bookings tells them their takings are
 * fake. A Production API can never report Sandbox — it refuses to start on that provider — so this
 * only ever appears where it is true.
 *
 * It sits in the shell rather than on the bookings screen because both sides of this console are
 * affected and neither is only about payments: an administrator reading a dashboard figure and a
 * dealer preparing a car for a "paid" booking are equally entitled to know.
 */
@Component({
  selector: 'kh-sandbox-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent],
  template: `
    @if (isSandbox()) {
      <div class="banner s-warn banner-tall sandbox-banner" role="status">
        <kh-icon name="warning" class="banner-icon" />
        <div class="banner-body">
          <div class="banner-title">{{ t('sandbox.title') }}</div>
          <div class="banner-text">{{ t('sandbox.body') }}</div>
        </div>
      </div>
    }
  `,
})
export class SandboxBannerComponent {
  protected readonly t = inject(I18nService).t;
  protected readonly isSandbox = inject(PlatformConfigService).isSandbox;
}
