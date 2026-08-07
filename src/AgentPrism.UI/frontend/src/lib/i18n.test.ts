import { describe, expect, it } from 'vitest';
import { interpolate, isLocale, matchLocale } from './i18n';
import { en } from '../locales/en';
import { tr } from '../locales/tr';

describe('interpolate', () => {
  it('substitutes a named placeholder', () => {
    expect(interpolate('Delete "{name}"?', { name: 'support' })).toBe('Delete "support"?');
  });

  it('substitutes the same placeholder more than once', () => {
    expect(interpolate('{n} of {n}', { n: 3 })).toBe('3 of 3');
  });

  it('leaves an unknown placeholder visible', () => {
    // A visible `{count}` on screen is a bug report; an empty gap is a mystery.
    expect(interpolate('{count} runs', {})).toBe('{count} runs');
  });

  it('returns the template unchanged when no parameters are given', () => {
    expect(interpolate('{count} runs')).toBe('{count} runs');
  });

  it('accepts a number as a value', () => {
    expect(interpolate('v{version}', { version: 4 })).toBe('v4');
  });
});

describe('matchLocale', () => {
  it('matches a regional tag on its primary subtag', () => {
    expect(matchLocale(['tr-TR'])).toBe('tr');
  });

  it('is case insensitive', () => {
    expect(matchLocale(['TR'])).toBe('tr');
  });

  it('takes the first supported tag in the list', () => {
    expect(matchLocale(['de-DE', 'fr', 'tr-TR', 'en'])).toBe('tr');
  });

  it('returns null when nothing is supported', () => {
    expect(matchLocale(['de-DE', 'fr'])).toBeNull();
  });

  it('returns null for an empty list', () => {
    expect(matchLocale([])).toBeNull();
  });
});

describe('isLocale', () => {
  it('accepts a shipped language', () => {
    expect(isLocale('en')).toBe(true);
    expect(isLocale('tr')).toBe(true);
  });

  it('rejects anything else', () => {
    expect(isLocale('de')).toBe(false);
    expect(isLocale(null)).toBe(false);
    expect(isLocale(undefined)).toBe(false);
  });
});

/**
 * The type system already refuses a `tr` that is missing a key — `tr: Messages`
 * makes that a compile error, which is the real gate.
 *
 * These tests cover what a type cannot: that no translation was left as a copy
 * of the English sentence by accident, and that both sides agree on which
 * placeholders a message takes.
 */
describe('catalogues', () => {
  const keys = Object.keys(en) as (keyof typeof en)[];

  it('has the same key set on both sides', () => {
    expect(Object.keys(tr).sort()).toEqual(keys.slice().sort());
  });

  it('uses the same placeholders in both languages', () => {
    const placeholders = (value: string): string[] =>
      [...value.matchAll(/\{(\w+)\}/g)].map((match) => match[1] as string).sort();

    for (const key of keys) {
      expect({ key, params: placeholders(tr[key]) }).toEqual({
        key,
        params: placeholders(en[key]),
      });
    }
  });

  it('gives every plural key both forms', () => {
    for (const key of keys) {
      if (key.endsWith('_one')) {
        expect(keys).toContain(`${key.slice(0, -4)}_other`);
      }
    }
  });

  it('translates every key that carries words', () => {
    // These messages are identical BY DESIGN. Every one of them is a technical
    // term the repository keeps in English — agent, tool, workflow, token,
    // prompt, MCP, OAuth — or a proper noun. Adding a key here is a decision,
    // not a shortcut: if a real translation was forgotten, this list is where
    // the shortcut would hide.
    const identicalOnPurpose = new Set<string>([
      'common.model',
      'common.agent',
      'nav.playground',
      'shell.language.en',
      'access.token.label',
      'access.token.placeholder',
      'palette.group.agent',
      'palette.group.workflow',
      'fields.temperature',
      'agentDetail.harness',
      'skills.frontmatter',
      'skills.grants.skill',
      'skills.grants.script',
      'runDetail.forWorkflow',
      'runDetail.forAgent',
      'workflows.column.workflow',
      'jobs.kind.workflow',
      'waterfall.spans_one',
      'graph.nodeTitle',
      'graph.legend.agent',
      'mcp.oauth',
      'mcp.oauthClientId',
      'settings.bearerToken',
      'settings.authorizationPolicy',
      'replay.model',
      'compare.model',
    ]);

    for (const key of keys) {
      if (identicalOnPurpose.has(key) || !/[a-z]{4}/.test(en[key])) {
        continue;
      }

      expect({ key, translated: tr[key] !== en[key] }).toEqual({ key, translated: true });
    }
  });
});
