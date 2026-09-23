import { DOCUMENT, Injectable, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { I18nService } from '../i18n/i18n.service';
import { Language, LANGUAGES } from '../i18n/language';
import { injectServerContext } from '../http/server-context';

export interface PageSeo {
  readonly title: string;
  readonly description?: string;
  /**
   * The page's path WITHOUT the language prefix and without a query (`cars/toyota-corolla-2024-…`).
   * Both language versions are named from it, as alternates of each other. Omit for a page that
   * must not be indexed.
   */
  readonly path?: string;
  readonly image?: string | null;
  readonly type?: 'website' | 'product' | 'profile';
  /** Account, booking and payment pages, and anything a crawler has no business keeping. */
  readonly noindex?: boolean;
  /** schema.org objects, rendered as JSON-LD. */
  readonly structuredData?: readonly object[];
}

const JSON_LD_ID = 'kh-structured-data';

/**
 * Everything a search engine or a link preview reads from a page's head.
 *
 * Written on the server, where it matters (the crawler and the preview bots read the HTML, not the
 * running app), and again on each navigation in the browser so the tab title and a shared link stay
 * right. The public origin comes from the renderer's configuration, never from the request's Host
 * header, which the visitor controls.
 */
@Injectable({ providedIn: 'root' })
export class SeoService {
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly document = inject(DOCUMENT);
  private readonly i18n = inject(I18nService);
  private readonly server = injectServerContext();

  set(input: PageSeo): void {
    // The isolate marks that keep a Latin name in place inside an Arabic sentence are for the page,
    // not for a tab title or a search result, where some renderers print them as boxes.
    const page: PageSeo = { ...input, title: plain(input.title), description: input.description === undefined ? undefined : plain(input.description) };
    const language = this.i18n.language();
    this.title.setTitle(page.title);
    this.tag('name', 'description', page.description);
    this.tag('name', 'robots', page.noindex ? 'noindex, nofollow' : 'index, follow');

    const indexable = !page.noindex && page.path !== undefined;
    const url = indexable ? this.absolute(language, page.path!) : null;
    this.links(indexable ? page.path! : null);

    this.tag('property', 'og:site_name', this.i18n.t('brand.name'));
    this.tag('property', 'og:type', page.type ?? 'website');
    this.tag('property', 'og:title', page.title);
    this.tag('property', 'og:description', page.description);
    this.tag('property', 'og:url', url ?? undefined);
    this.tag('property', 'og:locale', language === 'ar' ? 'ar_JO' : 'en_GB');
    this.tag('property', 'og:image', page.image ? this.absoluteAsset(page.image) : undefined);
    this.tag('name', 'twitter:card', page.image ? 'summary_large_image' : 'summary');

    this.structuredData(indexable ? (page.structuredData ?? []) : []);
  }

  /** The public URL of a page, for structured data. */
  absolute(language: Language, path: string): string {
    const clean = path.replace(/^\/+/, '');
    return `${this.origin()}/${language}${clean ? `/${clean}` : ''}`;
  }

  absoluteAsset(path: string): string {
    return /^https?:\/\//.test(path) ? path : `${this.origin()}${path.startsWith('/') ? '' : '/'}${path}`;
  }

  private origin(): string {
    const configured = this.server?.publicBaseUrl ?? this.document.location?.origin ?? '';
    return configured.replace(/\/$/, '');
  }

  private tag(attribute: 'name' | 'property', key: string, content: string | undefined): void {
    const selector = `${attribute}="${key}"`;
    if (content === undefined || content === '') {
      this.meta.removeTag(selector);
      return;
    }
    this.meta.updateTag({ [attribute]: key, content }, selector);
  }

  /** Canonical plus one alternate per language and `x-default`, which is Arabic (owner, 2026-09-23). */
  private links(path: string | null): void {
    const head = this.document.head;
    head.querySelectorAll('link[rel="canonical"], link[rel="alternate"][hreflang]').forEach((link) => link.remove());
    if (path === null) return;

    const add = (rel: string, href: string, hreflang?: string) => {
      const link = this.document.createElement('link');
      link.setAttribute('rel', rel);
      link.setAttribute('href', href);
      if (hreflang) link.setAttribute('hreflang', hreflang);
      head.appendChild(link);
    };

    add('canonical', this.absolute(this.i18n.language(), path));
    for (const language of LANGUAGES) add('alternate', this.absolute(language, path), language);
    add('alternate', this.absolute('ar', path), 'x-default');
  }

  private structuredData(objects: readonly object[]): void {
    this.document.getElementById(JSON_LD_ID)?.remove();
    if (objects.length === 0) return;
    const script = this.document.createElement('script');
    script.id = JSON_LD_ID;
    script.setAttribute('type', 'application/ld+json');
    // `<` escaped so text an office typed can never close the script element.
    script.textContent = JSON.stringify(objects.length === 1 ? objects[0] : objects).replace(/</g, '\\u003c');
    this.document.head.appendChild(script);
  }
}

function plain(text: string): string {
  return text.replace(/[⁦-⁩]/g, '');
}
