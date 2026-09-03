import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ICON_FALLBACK, ICON_PATHS, IconName } from './icon-paths';

/**
 * Renders one line icon from the design system's set.
 *
 * The SVG body comes from a fixed compile-time map, never from user input, which
 * is why bypassing the sanitizer here is safe: there is no path by which a caller
 * can inject markup. The icon paints with `currentColor` and sizes itself from
 * the surrounding `font-size`, so callers style it with ordinary text rules.
 */
@Component({
  selector: 'kh-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './icon.component.html',
  host: {
    style: 'display:inline-flex;align-items:center;justify-content:center;flex:none',
    '[attr.aria-hidden]': 'label() ? null : "true"',
    '[attr.role]': 'label() ? "img" : null',
    '[attr.aria-label]': 'label() || null',
  },
})
export class IconComponent {
  private readonly sanitizer = inject(DomSanitizer);

  readonly name = input.required<IconName | string>();
  /** Set only for a standalone icon that carries meaning of its own. */
  readonly label = input<string | undefined>(undefined);

  protected readonly svg = computed<SafeHtml>(() => {
    const body = ICON_PATHS[this.name()] ?? ICON_FALLBACK;
    return this.sanitizer.bypassSecurityTrustHtml(
      '<svg viewBox="0 0 24 24" width="1em" height="1em" stroke="currentColor" fill="none" ' +
        'stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" style="display:block">' +
        body +
        '</svg>',
    );
  });
}
