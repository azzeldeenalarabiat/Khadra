import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { I18nService } from '../../core/i18n/i18n.service';
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

  protected switch(): void {
    this.i18n.toggle();
  }
}
