import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Language } from './language';

/**
 * Tells the server the language a signed-in customer is reading in, so their emails and pushes follow it, as the
 * app's already do when a language is chosen. Sent at sign-in and on every switch (Wave 3, E3); a page merely opened
 * in the other language is not a choice, and changes nothing. Best effort: a failure here never undoes what the
 * customer did.
 */
export function reportLanguage(http: HttpClient, language: Language): Promise<void> {
  return firstValueFrom(http.put('/api/v1/auth/me/language', { language })).then(
    () => undefined,
    () => undefined,
  );
}
