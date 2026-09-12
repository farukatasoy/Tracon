import type { AgentSkillScriptDefinition } from '@tracon/client';
import type { AgentSkillResourceDefinition } from '../../lib/server-types';

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
