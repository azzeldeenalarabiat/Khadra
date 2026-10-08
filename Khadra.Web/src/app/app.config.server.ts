import { mergeApplicationConfig, ApplicationConfig } from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';
import { provideCspNonce } from './core/http/csp-nonce.server';

const serverConfig: ApplicationConfig = {
  providers: [
    provideServerRendering(withRoutes(serverRoutes)),
    // Pre-launch item 222: the nonce the customer BFF named in this page's script-src.
    provideCspNonce(),
  ],
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
