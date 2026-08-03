import { describe, expect, it } from 'vitest';
import { count, duration, latencyText, money, percent, prettyJson, relativeTime, shortId } from './format';
import { matchRoute, toRelativePath } from './router';

const NOW = Date.parse('2026-08-02T12:00:00Z');

describe('relativeTime', () => {
  it('formats seconds, minutes, hours and days', () => {
    expect(relativeTime('2026-08-02T11:59:48Z', NOW)).toBe('12s ago');
    expect(relativeTime('2026-08-02T11:56:00Z', NOW)).toBe('4m ago');
    expect(relativeTime('2026-08-02T09:00:00Z', NOW)).toBe('3h ago');
    expect(relativeTime('2026-07-30T12:00:00Z', NOW)).toBe('3d ago');
  });

  it('returns an em dash for missing or unparsable values', () => {
    expect(relativeTime(null, NOW)).toBe('—');
    expect(relativeTime('not a date', NOW)).toBe('—');
  });

  it('does not show negative time for clock skew', () => {
    expect(relativeTime('2026-08-02T12:00:30Z', NOW)).toBe('just now');
  });
});

describe('duration', () => {
  it('switches units by magnitude', () => {
    expect(duration('2026-08-02T12:00:00Z', '2026-08-02T12:00:00.340Z')).toBe('340ms');
    expect(duration('2026-08-02T12:00:00Z', '2026-08-02T12:00:01.240Z')).toBe('1.24s');
    expect(duration('2026-08-02T12:00:00Z', '2026-08-02T12:01:30Z')).toBe('1m 30s');
  });

  it('returns an em dash while a run is still going', () => {
    expect(duration('2026-08-02T12:00:00Z', null)).toBe('—');
  });
});

describe('count and percent', () => {
  it('distinguishes zero from unknown', () => {
    expect(count(0)).toBe('0');
    expect(count(null)).toBe('—');
    expect(percent(0)).toBe('0.0%');
    expect(percent(null)).toBe('—');
  });

  it('formats an error rate', () => {
    expect(percent(0.125)).toBe('12.5%');
  });
});

describe('money', () => {
  it('distinguishes an undefined price from zero', () => {
    expect(money(null, 'USD')).toBe('—');
    expect(money(0, 'USD')).toBe('0.00 USD');
  });

  it('appends the currency label with no conversion', () => {
    expect(money(1.5, 'USD')).toBe('1.50 USD');
    expect(money(1.5, null)).toBe('1.50');
  });

  it('shows enough precision for sub-cent per-token costs', () => {
    expect(money(0.123456, 'USD')).toBe('0.1235 USD');
  });
});

describe('latencyText', () => {
  it('switches units by magnitude', () => {
    expect(latencyText('00:00:00.3400000')).toBe('340ms');
    expect(latencyText('00:00:01.2400000')).toBe('1.24s');
  });

  it('returns an em dash for missing or unparsable values', () => {
    expect(latencyText(null)).toBe('—');
    expect(latencyText('not a timespan')).toBe('—');
  });
});

describe('shortId', () => {
  it('shortens long identifiers and leaves short ones alone', () => {
    expect(shortId('019fc02e-1234-7890-abcd-ef0123456789')).toBe('019fc02e…6789');
    expect(shortId('short')).toBe('short');
    expect(shortId(null)).toBe('—');
  });
});

describe('prettyJson', () => {
  it('formats JSON', () => {
    expect(prettyJson('{"a":1}')).toBe('{\n  "a": 1\n}');
  });

  it('returns the raw text when it is not JSON', () => {
    // Tool arguments are formatted by hand on the server to stay AOT friendly
    // and are not guaranteed to be valid JSON.
    expect(prettyJson('orderId=ORD-7')).toBe('orderId=ORD-7');
  });
});

describe('matchRoute', () => {
  it('matches literal paths', () => {
    expect(matchRoute('agents', 'agents')).toEqual({ path: 'agents', params: {} });
    expect(matchRoute('agents', 'runs')).toBeNull();
  });

  it('captures dynamic segments', () => {
    expect(matchRoute('runs/:id', 'runs/019fc0')).toEqual({
      path: 'runs/019fc0',
      params: { id: '019fc0' },
    });
  });

  it('decodes encoded segments', () => {
    expect(matchRoute('agents/:name', 'agents/my%20agent')?.params).toEqual({ name: 'my agent' });
  });

  it('does not match a different segment count', () => {
    expect(matchRoute('agents/:name', 'agents')).toBeNull();
    expect(matchRoute('agents', 'agents/x')).toBeNull();
  });

  it('lets a literal win when it is registered first', () => {
    // `agents/new` and `agents/:name` both match "agents/new"; the route table
    // relies on order, so the literal pattern must be tried first.
    expect(matchRoute('agents/new', 'agents/new')).not.toBeNull();
    expect(matchRoute('agents/:name', 'agents/new')).not.toBeNull();
  });
});

describe('toRelativePath', () => {
  it('treats the base with and without a trailing slash as the root', () => {
    // The address bar usually omits the trailing slash. Missing this case
    // rendered "page not found" on the most common entry URL there is.
    expect(toRelativePath('/agentprism', '/agentprism/')).toBe('');
    expect(toRelativePath('/agentprism/', '/agentprism/')).toBe('');
  });

  it('strips the base from nested paths', () => {
    expect(toRelativePath('/agentprism/runs/019fc0', '/agentprism/')).toBe('runs/019fc0');
    expect(toRelativePath('/panel/settings', '/panel/')).toBe('settings');
  });

  it('works when mounted at the site root', () => {
    expect(toRelativePath('/', '/')).toBe('');
    expect(toRelativePath('/agents', '/')).toBe('agents');
  });

  it('ignores a trailing slash on a nested path', () => {
    expect(toRelativePath('/agentprism/tools/', '/agentprism/')).toBe('tools');
  });
});
