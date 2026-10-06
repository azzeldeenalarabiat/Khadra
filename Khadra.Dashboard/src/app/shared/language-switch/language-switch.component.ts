import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { I18nService } from '../../core/i18n/i18n.service';
import { reportLanguage } from '../../core/services/report-language';
import { SessionService } from '../../core/services/session.service';
import { IconComponent } from '../icon/icon.component';

/**
 * The language switch: one icon, two languages, no menu.
 *
 * A dropdown would be the obvious shape and it is the wrong one here. There are exactly two
 * languages, so a menu costs a click to reach a list of one alternative. The button therefore says
 * what it will DO — it shows the language you are not in — which is the convention every
 * two-language site converges on.
 *
 * The label beside the icon is always written in its own script (English / العربية) so it is legible
 * to someone who cannot read the language currently on screen. That is the whole point: a person who
 * lands in the wrong language has to be able to find their way out of it.
 */
@Component({
  selector: 'kh-language-switch',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './language-switch.component.html',
  imports: [IconComponent],
})
export class LanguageSwitchComponent {
  private readonly i18n = inject(I18nService);
  private readonly session = inject(SessionService);
  private readonly http = inject(HttpClient);

  protected readonly t = this.i18n.t;
  protected readonly lang = this.i18n.lang;

  /** The language the button switches TO — what it offers, not where you are. */
  protected readonly other = computed(() => (this.lang() === 'ar' ? 'en' : 'ar'));
  protected readonly otherLabel = computed(() =>
    this.other() === 'ar' ? this.t('lang.ar') : this.t('lang.en'),
  );
  protected readonly description = computed(() =>
    this.other() === 'ar' ? this.t('lang.toArabic') : this.t('lang.toEnglish'),
  );

  /**
   * A switch while signed in is also told to the server, so the platform's emails follow it (Wave 3, C6); on the
   * sign-in page there is nobody to tell yet, and signing in reports the language then.
   */
  protected switch(): void {
    this.i18n.toggle();
    if (this.session.user()) void reportLanguage(this.http, this.i18n.lang());
  }
}
