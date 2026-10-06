import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Language } from '../i18n/language';

/**
 * Tells the server the language a signed-in person reads the console in, so the emails the platform sends them — an
 * office's alerts among them — arrive in it rather than in both languages (Fix & Polish Wave 3, C6). Sent at sign-in
 * and on every switch; the same `PUT /auth/me/language` the app and the website use. Best effort: a failure here
 * never undoes the sign-in or the switch.
 */
export function reportLanguage(http: HttpClient, language: Language): Promise<void> {
  return firstValueFrom(http.put('/api/v1/auth/me/language', { language })).then(
    () => undefined,
    () => undefined,
  );
}
