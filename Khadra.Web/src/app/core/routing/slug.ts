/**
 * Readable URLs for cars and rental offices, without a slug column anywhere.
 *
 * `/cars/toyota-corolla-2024-3f2b…` — the words are decoration, the id at the end is the address. A
 * page reads only the id, and when the words it was reached by differ from the words the record would
 * produce today (a typo, an old link, a price-less share) it redirects permanently to the current
 * form, so every car has exactly one indexable URL.
 *
 * The words are built from what the API already returns: make, model and year for a car; the business
 * name for an office (locked after approval, so stable). Only ASCII letters and digits survive; a name
 * written only in Arabic reduces to nothing and the URL is the id alone, rather than a percent-encoded
 * blob nobody can read or type.
 */
const GUID = /([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/i;

export function slugWords(...parts: readonly (string | number | null | undefined)[]): string {
  return parts
    .filter((part) => part !== null && part !== undefined && `${part}`.trim() !== '')
    .join(' ')
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 80)
    .replace(/-+$/g, '');
}

export function slugFor(id: string, ...parts: readonly (string | number | null | undefined)[]): string {
  const words = slugWords(...parts);
  return words ? `${words}-${id.toLowerCase()}` : id.toLowerCase();
}

/** The id at the end of a slug, or null when there is none (the page is then a 404). */
export function idFromSlug(slug: string | null | undefined): string | null {
  const match = GUID.exec(slug ?? '');
  return match ? match[1]!.toLowerCase() : null;
}
