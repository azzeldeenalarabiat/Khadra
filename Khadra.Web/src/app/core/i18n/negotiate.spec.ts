import { describe, expect, it } from 'vitest';
import { negotiateLanguage } from './negotiate';

describe('negotiateLanguage', () => {
  it('sends a browser that clearly prefers English to English', () => {
    expect(negotiateLanguage('en-US,en;q=0.9')).toBe('en');
    expect(negotiateLanguage('en-GB')).toBe('en');
    expect(negotiateLanguage('en-US,en;q=0.9,ar;q=0.8')).toBe('en');
  });

  it('sends everyone else to Arabic', () => {
    expect(negotiateLanguage('ar-JO,ar;q=0.9,en;q=0.8')).toBe('ar');
    expect(negotiateLanguage('fr-FR,en;q=0.5')).toBe('ar');
    expect(negotiateLanguage('de')).toBe('ar');
  });

  it('treats a missing or unreadable header as not clear', () => {
    expect(negotiateLanguage(undefined)).toBe('ar');
    expect(negotiateLanguage(null)).toBe('ar');
    expect(negotiateLanguage('')).toBe('ar');
    expect(negotiateLanguage('*')).toBe('ar');
    expect(negotiateLanguage(';;;,,')).toBe('ar');
  });

  it('reads weights rather than trusting the order they were written in', () => {
    expect(negotiateLanguage('ar;q=0.2,en;q=0.9')).toBe('en');
    expect(negotiateLanguage('en;q=0.3,ar;q=0.8')).toBe('ar');
  });

  it('does not call English clear when Arabic is preferred just as much', () => {
    expect(negotiateLanguage('en,ar')).toBe('ar');
    expect(negotiateLanguage('en;q=0.8,ar;q=0.8')).toBe('ar');
  });

  it('ignores a language the browser explicitly refuses', () => {
    expect(negotiateLanguage('en;q=0,ar')).toBe('ar');
    expect(negotiateLanguage('ar;q=0,en')).toBe('en');
  });
});
