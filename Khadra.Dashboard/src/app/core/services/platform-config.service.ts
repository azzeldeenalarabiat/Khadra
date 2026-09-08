import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { FormatService } from '../i18n/format.service';

/** The slice of `/api/v1/app-config` the console needs. The endpoint sends a good deal more. */
interface AppConfigResponse {
  readonly timeZone?: string;
  readonly currency?: { readonly code?: string; readonly minorUnits?: number };
}

/**
 * The facts about the platform that the console must not invent.
 *
 * Read once at startup from the same `/api/v1/app-config` the customer app reads, and pushed into
 * `FormatService` so every printed amount and date follows from it.
 *
 * It exists because of a real defect: the console printed a 110.000 JOD booking as "JOD 110" and a
 * 9.500 JOD delivery fee as "9.5 JOD". The dinar is divided into a THOUSAND fils, so a price without
 * its three decimals is not a tidier price, it is a different-looking one — and the customer app,
 * which does read this endpoint, was showing "JOD 110.000" for the same booking at the same moment.
 * Two surfaces disagreeing about a figure is worse than either format on its own.
 *
 * The console deliberately did not guess the scale before this: the comment in `money.ts` says so.
 * The fix is therefore to ASK, not to hard-code a 3 that would be wrong for the next currency.
 *
 * A failure here is not fatal. If the call does not answer, `FormatService` keeps its previous
 * behaviour — amounts print at whatever scale they arrived at — which is the same console people
 * were using yesterday. Blocking the whole application from starting over a formatting nicety would
 * be the worse trade.
 */
@Injectable({ providedIn: 'root' })
export class PlatformConfigService {
  private readonly http = inject(HttpClient);
  private readonly formats = inject(FormatService);

  async load(): Promise<void> {
    try {
      const config = await firstValueFrom(
        this.http.get<AppConfigResponse>('/api/v1/app-config'),
      );
      this.formats.useCurrencyMinorUnits(config?.currency?.minorUnits);
      this.formats.useTimeZone(config?.timeZone);
    } catch {
      // Deliberately swallowed. See the class comment: the console still works.
    }
  }
}
