import { describe, expect, it } from 'vitest';
import { emptyForm, withDefaultProvider } from './agent-editor';

describe('withDefaultProvider', () => {
  it('defaults to the first catalog provider when the form has none', () => {
    const next = withDefaultProvider(emptyForm, ['anthropic', 'openai']);

    expect(next.provider).toBe('anthropic');
  });

  it('leaves an empty form unchanged when the catalog is empty', () => {
    const next = withDefaultProvider(emptyForm, []);

    expect(next).toBe(emptyForm);
  });

  // HATA-S4-009 / MT-UIAG-014: the effect that loads an existing definition
  // and the effect that defaults the provider can both settle in the same
  // React commit. When that happens this function receives `current` as the
  // *already-loaded* form (the definition's real provider applied first),
  // not the stale pre-load form its own effect closed over. It must not
  // overwrite a provider that is already set, even though it differs from
  // the catalog's first entry.
  it('does not overwrite a provider already set by a concurrent update', () => {
    const loadedFromDefinition: typeof emptyForm = { ...emptyForm, provider: 'openai' };

    const next = withDefaultProvider(loadedFromDefinition, ['anthropic', 'google']);

    expect(next.provider).toBe('openai');
  });
});
