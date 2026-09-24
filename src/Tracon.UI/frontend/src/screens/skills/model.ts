import type { AgentSkillScriptDefinition } from '@tracon/client';
import type {
  AgentSkillDefinition,
  AgentSkillResourceDefinition,
  SkillScriptGrant,
} from '../../lib/server-types';

/**
 * The shape the skill editor edits.
 *
 * The generated request type makes every field but name/description/
 * instructions optional (omission means "use the server default"), but the form
 * always sends a fully-populated body — a local shape keeps the JSX's direct
 * field reads (`form.resources.map(...)`, …) free of `?? []` noise.
 */
export interface SkillForm {
  name: string;
  description: string;
  instructions: string;
  compatibility: string | null;
  license: string | null;
  allowedTools: string | null;
  metadata?: Record<string, never>;
  enabled: boolean;
  resources: AgentSkillResourceDefinition[];
  scripts: AgentSkillScriptDefinition[];
}

export const emptyResource = (): AgentSkillResourceDefinition => ({
  name: '',
  description: '',
  mediaType: 'text/plain',
  content: '',
});

export const emptyScript = (): AgentSkillScriptDefinition => ({
  name: '',
  description: '',
  extension: 'py',
  content: '',
  parametersSchema: null,
});

export const emptyRequest = (): SkillForm => ({
  name: '',
  description: '',
  instructions: '',
  compatibility: null,
  license: null,
  allowedTools: null,
  enabled: true,
  resources: [],
  scripts: [],
});

/**
 * The hash a grant pins today: the script's own hash for a script grant, the
 * fingerprint of the whole script set when the grant covers every script.
 * `null` when the skill has no script by that name.
 */
export function currentHash(skill: AgentSkillDefinition, scriptName: string | null | undefined): string | null {
  if (scriptName === null || scriptName === undefined || scriptName.length === 0) {
    return skill.scriptSetHash;
  }

  return skill.scripts.find((script) => script.name === scriptName)?.contentHash ?? null;
}

export type Pin = 'current' | 'stale' | 'disk';

/**
 * Whether a grant still authorizes the content that runs.
 *
 * Mirrors the runner: a stored or code script runs only under a grant whose hash
 * equals the current one (compared without regard to case), and a script read
 * from disk is not pinned at all. A grant with no hash therefore authorizes disk
 * scripts only — for a stored or code skill it is as good as none.
 */
export function pinOf(grant: SkillScriptGrant, skill: AgentSkillDefinition | null): Pin {
  if (skill === null) {
    return 'disk';
  }

  const current = currentHash(skill, grant.scriptName);

  return grant.contentHash !== null &&
    current !== null &&
    grant.contentHash.toUpperCase() === current.toUpperCase()
    ? 'current'
    : 'stale';
}
