import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ICON_FALLBACK, ICON_PATHS, IconName } from './icon-paths';

/**
 * Renders one line icon from the design system's set.
 *
 * The SVG body comes from a frozen compile-time map and `name` is typed to the
 * names in it, so no caller can reach the sanitizer bypass with markup of their
 * own. Sizing and layout live in global CSS rather than a style attribute, so a
 * strict Content-Security-Policy cannot blank every icon in the console.
 */
@Component({
  selector: 'kh-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './icon.component.html',
  host: {
    '[attr.aria-hidden]': 'label() ? null : "true"',
    '[attr.role]': 'label() ? "img" : null',
    '[attr.aria-label]': 'label() || null',
  },
})
export class IconComponent {
  private readonly sanitizer = inject(DomSanitizer);

  readonly name = input.required<IconName>();
  /** Set only for a standalone icon that carries meaning of its own. */
  readonly label = input<string | undefined>(undefined);

  protected readonly svg = computed<SafeHtml>(() => {
    const body = ICON_PATHS[this.name()] ?? ICON_FALLBACK;
    return this.sanitizer.bypassSecurityTrustHtml(
      '<svg viewBox="0 0 24 24" width="1em" height="1em" stroke="currentColor" fill="none" ' +
        'stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">' +
        body +
        '</svg>',
    );
  });
}
