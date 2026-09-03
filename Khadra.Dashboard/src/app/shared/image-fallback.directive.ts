import { Directive, ElementRef, HostListener, inject } from '@angular/core';

/**
 * A photo whose file is gone should look like "no photo", not like a broken-image icon.
 *
 * On load failure the <img> hides itself and its container gets `is-missing`, which the thumb rules
 * use to show the placeholder icon that sits beside it. Nothing is retried: a missing file is a fact
 * about storage, and the listing's record of it stays untouched.
 */
@Directive({ selector: 'img[khFallback]' })
export class ImageFallbackDirective {
  private readonly element = inject<ElementRef<HTMLImageElement>>(ElementRef);

  @HostListener('error')
  onError(): void {
    const img = this.element.nativeElement;
    img.hidden = true;
    img.parentElement?.classList.add('is-missing');
  }
}
