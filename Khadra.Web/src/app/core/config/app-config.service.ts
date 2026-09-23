import { HttpClient } from '@angular/common/http';
import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { AppConfig } from '../api/app-config.api';

/** How often the browser asks again while the API is not answering (the app's lesson: 10s). */
export const APP_CONFIG_RETRY_MS = 10_000;

/**
 * The platform's published facts: time zone, currency scale, date bounds, vocabularies, payment mode.
 *
 * Learned from the customer app's cold starts on Render (`Khadra.Mobile/lib/core/providers.dart`):
 * a sleeping API can take longer to wake than one request waits, so a failure is not an answer. In the
 * browser this keeps asking every ten seconds until it gets one, and screens that depend on a figure
 * from here show their loading state meanwhile rather than a guess. A success is kept for the page's
 * lifetime: nothing in it changes without an API restart.
 *
 * On the server it is asked for once per render (the renderer caches it for a minute), and a failure
 * leaves it null: the page renders its "not answering" state with a 503, and the browser picks up the
 * retry from there.
 */
@Injectable({ providedIn: 'root' })
export class AppConfigService {
  private readonly http = inject(HttpClient);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private readonly state = signal<AppConfig | null>(null);
  private readonly failed = signal(false);
  private inFlight: Promise<AppConfig | null> | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;

  readonly config = this.state.asReadonly();
  /** True while the last attempt failed and nothing has been received yet. */
  readonly unavailable = computed(() => this.state() === null && this.failed());

  load(): Promise<AppConfig | null> {
    if (this.state()) return Promise.resolve(this.state());
    this.inFlight ??= firstValueFrom(this.http.get<AppConfig>('/api/v1/app-config'))
      .then((config) => {
        this.state.set(config);
        this.failed.set(false);
        return config;
      })
      .catch(() => {
        this.failed.set(true);
        if (this.isBrowser) this.scheduleRetry();
        return null;
      })
      .finally(() => {
        this.inFlight = null;
      });
    return this.inFlight;
  }

  /** Ask now, instead of waiting for the next scheduled attempt. */
  retryNow(): Promise<AppConfig | null> {
    if (this.retryTimer) clearTimeout(this.retryTimer);
    this.retryTimer = null;
    return this.load();
  }

  private scheduleRetry(): void {
    if (this.retryTimer) return;
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      void this.load();
    }, APP_CONFIG_RETRY_MS);
  }
}
