import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input, signal, viewChild } from '@angular/core';
import { I18nService } from '../../core/i18n/i18n.service';
import { IconComponent } from '../icon/icon.component';

/**
 * A car's photos: one large, thumbnails beneath, a counter, and a full-screen view. Arrow keys move
 * through them on a desktop, a swipe does on a phone, and both follow the page's direction — in Arabic
 * the next photo is to the left.
 */
@Component({
  selector: 'kh-photo-gallery',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconComponent],
  templateUrl: './photo-gallery.component.html',
})
export class PhotoGalleryComponent {
  protected readonly i18n = inject(I18nService);

  readonly photos = input.required<readonly string[]>();
  readonly alt = input('');

  protected readonly index = signal(0);
  protected readonly current = computed(() => this.photos()[Math.min(this.index(), this.photos().length - 1)] ?? null);
  private readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('viewer');
  private swipeStart: number | null = null;

  protected go(delta: number): void {
    const count = this.photos().length;
    if (count === 0) return;
    this.index.set((this.index() + delta + count) % count);
  }

  protected show(index: number): void {
    this.index.set(index);
  }

  protected open(): void {
    this.dialog()?.nativeElement.showModal();
  }

  protected close(): void {
    this.dialog()?.nativeElement.close();
  }

  protected key(event: KeyboardEvent): void {
    // "Forward" is towards the end of the reading line: right in English, left in Arabic.
    const forward = this.i18n.isArabic() ? 'ArrowLeft' : 'ArrowRight';
    const back = this.i18n.isArabic() ? 'ArrowRight' : 'ArrowLeft';
    if (event.key === forward) {
      event.preventDefault();
      this.go(1);
    } else if (event.key === back) {
      event.preventDefault();
      this.go(-1);
    }
  }

  protected pointerDown(event: PointerEvent): void {
    this.swipeStart = event.clientX;
  }

  protected pointerUp(event: PointerEvent): void {
    if (this.swipeStart === null) return;
    const moved = event.clientX - this.swipeStart;
    this.swipeStart = null;
    if (Math.abs(moved) < 40) return;
    const towardsStart = this.i18n.isArabic() ? moved < 0 : moved > 0;
    this.go(towardsStart ? -1 : 1);
  }
}
