import {
  ChangeDetectionStrategy,
  Component,
  REQUEST_CONTEXT,
  provideZonelessChangeDetection,
  signal,
  ɵsetDocument as setDocument,
} from '@angular/core';
import { bootstrapApplication, provideClientHydration } from '@angular/platform-browser';
import {
  provideServerRendering,
  renderApplication,
  ɵENABLE_DOM_EMULATION as ENABLE_DOM_EMULATION,
} from '@angular/platform-server';
import { afterEach, describe, expect, it } from 'vitest';
import { EVENT_DISPATCH_SCRIPT_ID, cspNonceFrom, nonceEventDispatchContract } from './csp-nonce';
import { provideCspNonce } from './csp-nonce.server';
import { ServerRenderContext } from './server-context';

/** As the customer BFF mints one: 16 random bytes, base64. */
const NONCE = 'q3Vb8l0Vw6mZ1c2bQe9xkA==';

describe('cspNonceFrom (pre-launch item 222)', () => {
  it('believes a nonce shaped as the BFF mints it', () => {
    expect(cspNonceFrom(NONCE)).toBe(NONCE);
  });

  it.each([undefined, null, '', 'short', '"><script>alert(1)</script>', `${NONCE}'`, 'a b c d e f g h i j k l m n'])(
    'prints nothing for %j',
    (value) => {
      expect(cspNonceFrom(value)).toBeNull();
    },
  );
});

describe('nonceEventDispatchContract', () => {
  const page = () => {
    const doc = document.implementation.createHTMLDocument('page');
    const script = doc.createElement('script');
    script.id = EVENT_DISPATCH_SCRIPT_ID;
    doc.head.append(script);
    return { doc, script };
  };

  it("puts the page's nonce on the build's contract script", () => {
    const { doc, script } = page();
    nonceEventDispatchContract(doc, NONCE);
    expect(script.getAttribute('nonce')).toBe(NONCE);
  });

  it('leaves the script alone without a nonce, and a page without the script alone', () => {
    const { doc, script } = page();
    nonceEventDispatchContract(doc, null);
    expect(script.hasAttribute('nonce')).toBe(false);
    expect(() => nonceEventDispatchContract(document.implementation.createHTMLDocument('none'), NONCE)).not.toThrow();
  });
});

/**
 * A real server render, as the renderer does it: the page has a click listener, so Angular keeps the build's
 * event-dispatch contract and adds its replay call after it. Under the customer BFF's `script-src`, both run only if
 * both carry the nonce the BFF named — the defect was that neither did.
 */
@Component({
  selector: 'kh-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<button type="button" (click)="count.set(count() + 1)">{{ count() }}</button>`,
})
class PageWithAClick {
  protected readonly count = signal(0);
}

/** The built `index.html` in miniature: the contract the build inlines, and the root the page renders into. */
function index(): Document {
  const doc = document.implementation.createHTMLDocument('');
  const contract = doc.createElement('script');
  contract.type = 'text/javascript';
  contract.id = EVENT_DISPATCH_SCRIPT_ID;
  contract.textContent = '(()=>{window.__jsaction_bootstrap=function(){}})()';
  doc.head.append(contract);
  doc.body.append(doc.createElement('kh-root'));
  return doc;
}

/**
 * Rendered with Angular's DOM emulation off, into a document of this test's own. With it on, the server platform
 * copies its emulated DOM classes over the test runner's globals (`Event` among them), which outlive this file and
 * break every later spec that dispatches an event in the same worker.
 */
async function render(context: ServerRenderContext | null): Promise<Document> {
  const html = await renderApplication(
    (bootstrapContext) =>
      bootstrapApplication(
        PageWithAClick,
        {
          providers: [
            provideZonelessChangeDetection(),
            provideClientHydration(),
            provideServerRendering(),
            provideCspNonce(),
            { provide: REQUEST_CONTEXT, useValue: context },
          ],
        },
        bootstrapContext,
      ),
    { document: index(), url: '/en', platformProviders: [{ provide: ENABLE_DOM_EMULATION, useValue: false }] },
  );
  return new DOMParser().parseFromString(html, 'text/html');
}

const inlineScripts = (doc: Document) =>
  [...doc.querySelectorAll('script:not([src])')].filter((script) => script.getAttribute('type') !== 'application/json');

describe('a server-rendered page under the BFF nonce (pre-launch item 222)', () => {
  // The server platform names the document it rendered as Angular's current one; hand that back to the runner's own.
  afterEach(() => setDocument(undefined));

  const context = (cspNonce: string | null): ServerRenderContext => ({
    apiBaseUrl: 'http://api.test',
    publicBaseUrl: 'https://www.khadra.test',
    clientAddress: null,
    cspNonce,
  });

  it('prints the nonce on the contract and on the replay call, and on no other script', async () => {
    const scripts = inlineScripts(await render(context(NONCE)));

    expect(scripts.map((script) => script.id || 'replay')).toEqual([EVENT_DISPATCH_SCRIPT_ID, 'replay']);
    expect(scripts[1].textContent).toContain('__jsaction_bootstrap');
    for (const script of scripts) expect(script.getAttribute('nonce')).toBe(NONCE);
  });

  it('prints no nonce when the request did not come through the BFF', async () => {
    for (const doc of [await render(context(null)), await render(null)]) {
      const scripts = inlineScripts(doc);
      expect(scripts.length).toBe(2);
      for (const script of scripts) expect(script.hasAttribute('nonce')).toBe(false);
    }
  });
});
