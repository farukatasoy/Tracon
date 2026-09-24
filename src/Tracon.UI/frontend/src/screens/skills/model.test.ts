import { describe, expect, it } from 'vitest';
import type { AgentSkillDefinition, SkillScriptGrant } from '../../lib/server-types';
import { currentHash, pinOf } from './model';

const SCRIPT_HASH = 'A'.repeat(64);
const SET_HASH = 'B'.repeat(64);

const skill: AgentSkillDefinition = {
  id: '00000000-0000-0000-0000-000000000001',
  tenantId: 'default',
  name: 'invoice-analysis',
  description: 'Reads invoices.',
  instructions: 'Run the total script.',
  metadata: {},
  enabled: true,
  version: 1,
  resources: [],
  scripts: [{ name: 'total', extension: 'sh', content: 'echo total', contentHash: SCRIPT_HASH }],
  scriptSetHash: SET_HASH,
  origin: 'Database',
  createdAt: '2026-09-24T00:00:00Z',
  updatedAt: '2026-09-24T00:00:00Z',
};

const grant = (overrides: Partial<SkillScriptGrant>): SkillScriptGrant => ({
  id: '00000000-0000-0000-0000-000000000002',
  tenantId: 'default',
  skillName: 'invoice-analysis',
  scriptName: 'total',
  contentHash: SCRIPT_HASH,
  grantedAt: '2026-09-24T00:00:00Z',
  revokedAt: null,
  ...overrides,
});

describe('currentHash', () => {
  it('pins the script hash for a script grant and the set fingerprint for a skill-wide one', () => {
    expect(currentHash(skill, 'total')).toBe(SCRIPT_HASH);
    expect(currentHash(skill, null)).toBe(SET_HASH);
    expect(currentHash(skill, '')).toBe(SET_HASH);
  });

  it('has nothing to pin for a script the skill does not carry', () => {
    expect(currentHash(skill, 'missing')).toBeNull();
  });
});

// The badge has to agree with the runner, or the console calls a grant current
// while every run under it is refused.
describe('pinOf', () => {
  it('reads a matching hash as current, whatever its case', () => {
    expect(pinOf(grant({}), skill)).toBe('current');
    expect(pinOf(grant({ contentHash: SCRIPT_HASH.toLowerCase() }), skill)).toBe('current');
    expect(pinOf(grant({ scriptName: null, contentHash: SET_HASH }), skill)).toBe('current');
  });

  it('reads changed content, a missing script and a grant with no hash as stale', () => {
    expect(pinOf(grant({ contentHash: 'C'.repeat(64) }), skill)).toBe('stale');
    expect(pinOf(grant({ scriptName: 'removed' }), skill)).toBe('stale');
    expect(pinOf(grant({ contentHash: null }), skill)).toBe('stale');
  });

  it('does not mix the two kinds of hash', () => {
    expect(pinOf(grant({ scriptName: null, contentHash: SCRIPT_HASH }), skill)).toBe('stale');
    expect(pinOf(grant({ contentHash: SET_HASH }), skill)).toBe('stale');
  });

  it('reads a name no stored or code skill carries as disk only, pinned or not', () => {
    expect(pinOf(grant({ contentHash: null }), null)).toBe('disk');
    expect(pinOf(grant({}), null)).toBe('disk');
  });
});
