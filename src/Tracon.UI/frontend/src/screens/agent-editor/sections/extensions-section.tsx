import type { UseQueryResult } from '@tanstack/react-query';
import type { Dispatch, ReactNode, SetStateAction } from 'react';
import type { AgentExtensionResponse } from '@tracon/client';
import { useT } from '../../../lib/i18n';
import { Badge, ErrorNote, Loading, Mono, Panel } from '../../../components/ui';
import type { FormState } from '../model';

/**
 * Picks the agent extensions an agent is compiled with. An extension is code the host
 * registered with `AddAgentExtension`; the console selects it by name and cannot write it.
 *
 * The order of the list is the order the extensions run, so a newly checked name goes to the
 * end. A name the definition holds that the host no longer registers stays on screen, checked
 * and marked, so the user can remove it: the server refuses to save a name it cannot find.
 */
export function ExtensionsSection({
  form,
  setForm,
  extensions,
}: {
  form: FormState;
  setForm: Dispatch<SetStateAction<FormState>>;
  extensions: UseQueryResult<AgentExtensionResponse[]>;
}): ReactNode {
  const t = useT();
  const registered = extensions.data ?? [];
  const known = new Set(registered.map((extension) => extension.name));
  const missing = extensions.isSuccess ? form.extensionNames.filter((name) => !known.has(name)) : [];

  const toggle = (name: string, checked: boolean): void =>
    setForm({
      ...form,
      extensionNames: checked
        ? form.extensionNames.filter((item) => item !== name)
        : [...form.extensionNames, name],
    });

  return (
    <Panel title={t('agentEditor.extensions')}>
      <div className="p-4">
        <p className="mb-3 text-sm text-muted">{t('agentEditor.extensionsNotice')}</p>

        {extensions.isPending && <Loading rows={2} />}
        {extensions.isError && <ErrorNote error={extensions.error} onRetry={() => void extensions.refetch()} />}

        {extensions.isSuccess && registered.length === 0 && missing.length === 0 && (
          <p className="text-base text-subtle">
            {t('agentEditor.noExtensions')} <Mono>AddAgentExtension(...)</Mono>
          </p>
        )}

        <div className="flex flex-col gap-1.5">
          {registered.map((extension) => {
            const checked = form.extensionNames.includes(extension.name);

            return (
              <label
                key={extension.name}
                className="flex cursor-pointer items-start gap-2.5 rounded-md border border-line px-3 py-2 hover:bg-raised"
              >
                <input
                  type="checkbox"
                  className="mt-0.5 accent-[var(--tracon-accent)]"
                  checked={checked}
                  onChange={() => toggle(extension.name, checked)}
                />
                <span className="min-w-0">
                  <Mono className="font-medium">{extension.name}</Mono>
                  {extension.description && <span className="block text-sm text-muted">{extension.description}</span>}
                </span>
              </label>
            );
          })}
          {missing.map((name) => (
            <label
              key={name}
              className="flex cursor-pointer items-start gap-2.5 rounded-md border border-line px-3 py-2 hover:bg-raised"
            >
              <input
                type="checkbox"
                className="mt-0.5 accent-[var(--tracon-accent)]"
                checked
                onChange={() => toggle(name, true)}
              />
              <span className="min-w-0">
                <Mono className="font-medium">{name}</Mono>
                <Badge tone="warn">{t('agentEditor.extensionNotRegistered')}</Badge>
              </span>
            </label>
          ))}
        </div>
      </div>
    </Panel>
  );
}
