import { describe, expect, it } from 'vitest';
import { enAgents } from './en/agents';
import { enCommon } from './en/common';
import { enOperations } from './en/operations';
import { enRuns } from './en/runs';
import { enSettings } from './en/settings';
import { enWorkflows } from './en/workflows';
import { en } from './en';
import { tr } from './tr';

/**
 * Object spread silently lets a later fragment override an earlier one on a
 * duplicate key (K-doc §109.3) — `tsc` cannot catch this, since the spread's
 * inferred type just takes the last writer. This test is the only thing that
 * does.
 */
describe('locale fragments', () => {
  const fragments = {
    common: enCommon,
    agents: enAgents,
    runs: enRuns,
    workflows: enWorkflows,
    operations: enOperations,
    settings: enSettings,
  };

  it('carries no key in more than one fragment', () => {
    const seenIn = new Map<string, string>();
    const duplicates: string[] = [];

    for (const [fragmentName, fragment] of Object.entries(fragments)) {
      for (const key of Object.keys(fragment)) {
        const owner = seenIn.get(key);

        if (owner !== undefined) {
          duplicates.push(`${key} (in ${owner} and ${fragmentName})`);
        } else {
          seenIn.set(key, fragmentName);
        }
      }
    }

    expect(duplicates).toEqual([]);
  });

  it('aggregates every fragment key into the English catalogue with none lost', () => {
    const fragmentKeyCount = Object.values(fragments).reduce(
      (sum, fragment) => sum + Object.keys(fragment).length,
      0,
    );

    expect(Object.keys(en)).toHaveLength(fragmentKeyCount);
  });

  it('gives the Turkish catalogue the exact same key set as English', () => {
    expect(Object.keys(tr).sort()).toEqual(Object.keys(en).sort());
  });
});
