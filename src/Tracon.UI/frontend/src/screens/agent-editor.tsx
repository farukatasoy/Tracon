import type { ReactNode } from 'react';
import { useT } from '../lib/i18n';
import { Button, ErrorNote, Loading, PageHeader } from '../components/ui';
import { CallableAgentsSection } from './agent-editor/sections/callable-agents-section';
import { ContextSection } from './agent-editor/sections/context-section';
import { ExtensionsSection } from './agent-editor/sections/extensions-section';
import { HarnessSection } from './agent-editor/sections/harness-section';
import { IdentitySection } from './agent-editor/sections/identity-section';
import { InstructionsSection } from './agent-editor/sections/instructions-section';
import { LoopSection } from './agent-editor/sections/loop-section';
import { ModelSection } from './agent-editor/sections/model-section';
import { PreviewSection } from './agent-editor/sections/preview-section';
import { SkillsSection } from './agent-editor/sections/skills-section';
import { ToolsSection } from './agent-editor/sections/tools-section';
import { ValidationReportPanel } from './agent-editor/sections/validation-report';
import { useAgentEditor } from './agent-editor/use-agent-editor';

/**
 * Create and edit stored agent definitions.
 *
 * Tools are picked from a list, never typed. A definition can only point at a
 * tool that is registered in code — that boundary is what keeps the console
 * from becoming a way to run arbitrary code on the server (rule K2).
 *
 * This screen is a route facade only: `useAgentEditor` owns state and the API
 * calls, each `Panel` below is its own section component. Neither queries nor
 * mutations happen here.
 */
export function AgentEditorScreen({ name }: { name?: string }): ReactNode {
  const t = useT();
  const editor = useAgentEditor(name);
  const { editing, ready, form, setForm } = editor;

  if (editing && editor.existing.isError) {
    return (
      <>
        <PageHeader title={t('agentEditor.editTitle', { name: name ?? '' })} />
        <ErrorNote error={editor.existing.error} onRetry={() => void editor.existing.refetch()} />
      </>
    );
  }

  if (editing && !ready) {
    return <Loading rows={8} label={t('agentEditor.loading')} />;
  }

  return (
    <>
      <PageHeader
        title={editing ? t('agentEditor.editTitle', { name: name ?? '' }) : t('agentEditor.newTitle')}
        description={editing ? t('agentEditor.editDescription') : t('agentEditor.newDescription')}
        actions={
          <>
            <Button onClick={editor.cancel}>{t('common.cancel')}</Button>
            <Button
              testId="agent-validate"
              busy={editor.validate.isPending}
              disabled={!editor.valid}
              onClick={() => editor.validate.mutate()}
            >
              {t('agentEditor.validate')}
            </Button>
            <Button
              tone="primary"
              testId="agent-save"
              busy={editor.save.isPending}
              disabled={!editor.valid}
              onClick={() => editor.save.mutate()}
            >
              {editing ? t('agentEditor.saveVersion') : t('agentEditor.create')}
            </Button>
          </>
        }
      />

      {/* Retry is Save (or Validate) again, so it holds back whenever the buttons do. The request is
          built from the form as it is now: after a failed save, a seed that is no longer a safe
          integer or a schema that is no longer JSON would be sent as an agent without it. */}
      {editor.save.isError && (
        <div className="mb-4">
          <ErrorNote
            error={editor.save.error}
            onRetry={editor.valid ? () => editor.save.mutate() : undefined}
          />
        </div>
      )}
      {editor.validate.isError && (
        <div className="mb-4">
          <ErrorNote
            error={editor.validate.error}
            onRetry={editor.valid ? () => editor.validate.mutate() : undefined}
          />
        </div>
      )}
      {editor.validate.data && <div className="mb-4"><ValidationReportPanel report={editor.validate.data} /></div>}

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="flex flex-col gap-4">
          <IdentitySection form={form} setForm={setForm} editing={editing} />
          <InstructionsSection form={form} setForm={setForm} />
          <ModelSection
            form={form}
            setForm={setForm}
            providers={editor.providers.data ?? []}
            models={editor.models}
            schemaJsonValid={editor.schemaJsonValid}
            hostedToolNames={(editor.tools.data ?? []).filter((tool) => tool.hostedKind != null).map((tool) => tool.name)}
          />
          <ToolsSection form={form} setForm={setForm} tools={editor.tools} />
          <SkillsSection form={form} setForm={setForm} skills={editor.skills} />
          <ExtensionsSection form={form} setForm={setForm} extensions={editor.extensions} />
          <CallableAgentsSection form={form} setForm={setForm} agents={editor.agents} />
          <HarnessSection form={form} setForm={setForm} />
          {form.harnessEnabled && (
            <LoopSection form={form} setForm={setForm} catalog={editor.loopCriteria} agents={editor.agents} />
          )}
          <ContextSection form={form} setForm={setForm} />
        </div>

        <div className="lg:sticky lg:top-16 lg:self-start">
          <PreviewSection request={editor.request} editing={editing} name={name} />
        </div>
      </div>
    </>
  );
}
