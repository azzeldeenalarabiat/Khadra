import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ConsentGateService, isConsentPending } from './consent-gate.service';

/**
 * Any request refused with 403 `legal.consent_pending` raises the consent prompt (Wave 4, W4-8): a legal text was
 * published while this console was open, and the server now answers nothing else until it is accepted. The refusal
 * itself still reaches the caller, which shows its ordinary failure until the prompt takes the page.
 */
export const consentGateInterceptor: HttpInterceptorFn = (request, next) => {
  const gate = inject(ConsentGateService);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (isConsentPending(error)) gate.raise();
      return throwError(() => error);
    }),
  );
};
