import { describe, expect, it } from 'vitest';
import { fuzzyScore, rankCommands, type CommandLike } from './palette';

const command = (label: string, keywords?: string): CommandLike =>
  keywords === undefined ? { label, group: 'Go to' } : { label, group: 'Go to', keywords };

describe('fuzzyScore', () => {
  it('matches a subsequence', () => {
    expect(fuzzyScore('Dashboard', 'dsb')).not.toBeNull();
  });

  it('returns null when a character is missing', () => {
    expect(fuzzyScore('Dashboard', 'dxb')).toBeNull();
  });

  it('returns null when the characters are in the wrong order', () => {
    // Matching is a left-to-right subsequence, not a bag of letters: the only
    // `h` in "dashboard" comes before its only `o`.
    expect(fuzzyScore('Dashboard', 'oh')).toBeNull();
  });

  it('scores an empty query as zero rather than rejecting it', () => {
    expect(fuzzyScore('Dashboard', '')).toBe(0);
  });

  it('ignores case', () => {
    expect(fuzzyScore('Dashboard', 'DASH')).toBe(fuzzyScore('dashboard', 'dash'));
  });

  it('ignores spaces in the query', () => {
    expect(fuzzyScore('Dashboard', 'd a s')).toBe(fuzzyScore('Dashboard', 'das'));
  });

  it('prefers an adjacent run over a scattered match', () => {
    const adjacent = fuzzyScore('runs', 'run');
    const scattered = fuzzyScore('rebuild unused nodes', 'run');

    expect(adjacent).toBeGreaterThan(scattered as number);
  });

  it('prefers a match at a word boundary', () => {
    const boundary = fuzzyScore('new agent', 'ag');
    const inside = fuzzyScore('managed', 'ag');

    expect(boundary).toBeGreaterThan(inside as number);
  });
});

describe('rankCommands', () => {
  it('keeps the declared order for an empty query', () => {
    const commands = [command('Runs'), command('Agents'), command('Dashboard')];

    expect(rankCommands(commands, '  ').map((entry) => entry.label)).toEqual([
      'Runs',
      'Agents',
      'Dashboard',
    ]);
  });

  it('drops a command that does not match', () => {
    const commands = [command('Runs'), command('Agents')];

    expect(rankCommands(commands, 'ag').map((entry) => entry.label)).toEqual(['Agents']);
  });

  it('orders a closer match first', () => {
    const commands = [command('Manage sessions'), command('Agents')];

    expect(rankCommands(commands, 'agent')[0]?.label).toBe('Agents');
  });

  it('matches on keywords that are not shown', () => {
    const commands = [command('Gösterge Paneli', 'Dashboard dashboard')];

    expect(rankCommands(commands, 'dashboard')).toHaveLength(1);
  });

  it('keeps the declared order when two commands tie', () => {
    const commands = [command('agent one'), command('agent two')];

    expect(rankCommands(commands, 'agent').map((entry) => entry.label)).toEqual([
      'agent one',
      'agent two',
    ]);
  });

  it('finds a run by the first characters of its id', () => {
    const commands = [command('support', '019fc02e-77aa-7b13-9f4c-2f7a2a2e5f21')];

    expect(rankCommands(commands, '019fc02e')).toHaveLength(1);
  });
});
