import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { en, type Messages } from '../locales/en';
import { tr } from '../locales/tr';

/**
 * A ~150 line localisation layer.
 *
 * `react-i18next` and `i18next` cost 15–25 KB gzipped and bring plural rule
 * tables, namespaces and lazy loading that two closely related languages do not
 * need. The same reasoning as the hand-written router (K-045): the small thing
 * that fits is cheaper than the general thing that does not.
 *
 * What is NOT translated here, on purpose:
 *  - server responses. `ProblemDetails` text stays English; the package is
 *    published internationally and the API contract is single-language. The UI
 *    translates the error titles it knows and shows the rest verbatim.
 *  - agent, tool and model names. They are identifiers, not prose.
 */

/** Languages the console ships with. */
export const LOCALES = ['en', 'tr'] as const;

export type Locale = (typeof LOCALES)[number];

export type MessageKey = keyof Messages;

/** Values substituted into `{placeholder}` slots. */
export type MessageParams = Readonly<Record<string, string | number>>;

/**
 * Keys that come in a singular/plural pair.
 *
 * A base key qualifies only when BOTH `_one` and `_other` exist, so `plural()`
 * cannot be pointed at a key whose other half was never written.
 */
export type PluralKey = {
  [K in MessageKey]: K extends `${infer Base}_one`
    ? `${Base}_other` extends MessageKey
      ? Base
      : never
    : never;
}[MessageKey];

const CATALOGUES: Record<Locale, Messages> = { en, tr };

const STORAGE_KEY = 'tracon.locale';

/**
 * The language `translate` reads.
 *
 * Module state, not React state: `translate` has to be callable from plain
 * functions and — more importantly — has to keep ONE identity for the whole
 * session. See `useT`.
 */
let active: Locale = 'en';

export function activeLocale(): Locale {
  return active;
}

export function isLocale(value: string | null | undefined): value is Locale {
  return value === 'en' || value === 'tr';
}

/** First supported language in a list of BCP 47 tags: `tr-TR` matches `tr`. */
export function matchLocale(tags: readonly string[]): Locale | null {
  for (const tag of tags) {
    const primary = tag.toLowerCase().split('-')[0] ?? '';

    if (isLocale(primary)) {
      return primary;
    }
  }

  return null;
}

/**
 * Reads the stored language preference.
 *
 * `localStorage`, while the bearer token lives in `sessionStorage` (K-047): a
 * language is not a secret, and a preference that had to be set again in every
 * new tab would not be a preference at all.
 */
export function readLocalePreference(): Locale | null {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);

    return isLocale(stored) ? stored : null;
  } catch {
    // Private browsing modes can throw on storage access.
    return null;
  }
}

export function writeLocalePreference(locale: Locale): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, locale);
  } catch {
    // A language that does not survive a reload beats a broken page.
  }
}

/** Stored preference, else the browser language, else English. */
export function detectLocale(): Locale {
  const stored = readLocalePreference();

  if (stored !== null) {
    return stored;
  }

  if (typeof navigator === 'undefined') {
    return 'en';
  }

  const tags = navigator.languages ?? [navigator.language];

  return matchLocale(tags) ?? 'en';
}

/**
 * Substitutes `{name}` placeholders.
 *
 * An unknown placeholder is left in place rather than blanked: a visible
 * `{count}` on screen is a bug report, an empty gap is a mystery.
 */
export function interpolate(template: string, params?: MessageParams): string {
  if (params === undefined) {
    return template;
  }

  return template.replace(/\{(\w+)\}/g, (match, name: string) =>
    Object.hasOwn(params, name) ? String(params[name]) : match,
  );
}

/** Looks up a message in the active language. */
export function translate(key: MessageKey, params?: MessageParams): string {
  return interpolate(CATALOGUES[active][key], params);
}

/**
 * Picks the singular or the plural form.
 *
 * `n === 1 ? one : other` covers English and Turkish. A language with a richer
 * rule set would need `Intl.PluralRules`, which is in the browser already — the
 * change is a few lines, and it is not made before it is needed.
 */
export function plural(key: PluralKey, n: number, params?: MessageParams): string {
  const variant = `${key}${n === 1 ? '_one' : '_other'}` as MessageKey;

  return translate(variant, { n: formatNumber(n), ...params });
}

/* --------------------------------------------------------------- formatting */

const numberFormats = new Map<string, Intl.NumberFormat>();
const dateFormats = new Map<string, Intl.DateTimeFormat>();
const relativeFormats = new Map<Locale, Intl.RelativeTimeFormat>();

function numberFormat(options?: Intl.NumberFormatOptions): Intl.NumberFormat {
  const cacheKey = `${active}|${JSON.stringify(options ?? {})}`;
  let format = numberFormats.get(cacheKey);

  if (format === undefined) {
    format = new Intl.NumberFormat(active, options);
    numberFormats.set(cacheKey, format);
  }

  return format;
}

/** Locale-aware number. Turkish writes `1.234,5` where English writes `1,234.5`. */
export function formatNumber(value: number, options?: Intl.NumberFormatOptions): string {
  return numberFormat(options).format(value);
}

/** Locale-aware absolute timestamp. */
export function formatDateTime(value: Date, options?: Intl.DateTimeFormatOptions): string {
  const resolved = options ?? { dateStyle: 'medium' as const, timeStyle: 'medium' as const };
  const cacheKey = `${active}|${JSON.stringify(resolved)}`;
  let format = dateFormats.get(cacheKey);

  if (format === undefined) {
    format = new Intl.DateTimeFormat(active, resolved);
    dateFormats.set(cacheKey, format);
  }

  return format.format(value);
}

/** Locale-aware relative time, e.g. `4 min. ago` in English or the Turkish equivalent in `tr`. */
export function formatRelative(value: number, unit: Intl.RelativeTimeFormatUnit): string {
  let format = relativeFormats.get(active);

  if (format === undefined) {
    format = new Intl.RelativeTimeFormat(active, { style: 'short', numeric: 'auto' });
    relativeFormats.set(active, format);
  }

  return format.format(value, unit);
}

/* -------------------------------------------------------------------- react */

interface LocaleValue {
  locale: Locale;
  setLocale: (next: Locale) => void;
}

const LocaleContext = createContext<LocaleValue | null>(null);

/**
 * Applies the language to the document.
 *
 * `lang` is not decoration: screen readers pick a voice from it, and so does
 * the browser's own hyphenation.
 */
export function applyDocumentLocale(locale: Locale): void {
  // Guarded so the pure parts of this module stay usable without a DOM — the
  // unit tests run in Node, and formatting is what they exercise.
  if (typeof document === 'undefined') {
    return;
  }

  document.documentElement.lang = locale;
}

/**
 * Resolves the starting language and applies it before React renders.
 *
 * Called from `main.tsx` for the same reason `applyTheme` is: deciding inside a
 * component would render one frame in the wrong language.
 */
export function initialiseLocale(): Locale {
  active = detectLocale();
  applyDocumentLocale(active);

  return active;
}

export function LocaleProvider({ children }: { children: ReactNode }): ReactNode {
  const [locale, setLocaleState] = useState<Locale>(activeLocale);

  const setLocale = useCallback((next: Locale) => {
    // Module state first: `translate` is synchronous and is called during the
    // render that this state change triggers.
    active = next;
    applyDocumentLocale(next);
    writeLocalePreference(next);
    setLocaleState(next);
  }, []);

  useEffect(() => {
    applyDocumentLocale(locale);
  }, [locale]);

  const value = useMemo<LocaleValue>(() => ({ locale, setLocale }), [locale, setLocale]);

  return <LocaleContext.Provider value={value}>{children}</LocaleContext.Provider>;
}

export function useLocale(): LocaleValue {
  const value = useContext(LocaleContext);

  if (value === null) {
    throw new Error('useLocale must be used inside LocaleProvider.');
  }

  return value;
}

/**
 * The translation function, plus a subscription to language changes.
 *
 * 🚨 The returned function is the module-level `translate` and therefore has a
 * STABLE identity. A fresh closure per language would land in the dependency
 * array of every `useCallback` that formats text — including `start` and `stop`
 * in the conversation panel, whose rebuild would tear down an open WebSocket.
 * Reading the context is what re-renders the component; the function itself
 * never has to change.
 */
export function useT(): typeof translate {
  useLocale();

  return translate;
}

/** The plural helper, subscribed the same way and stable for the same reason. */
export function usePlural(): typeof plural {
  useLocale();

  return plural;
}
