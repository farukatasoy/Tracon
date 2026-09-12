import { useEffect, useMemo, useState, type Dispatch, type SetStateAction } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../../lib/api';
import { useNavigate } from '../../lib/router';
import type {
  AgentDefinition,
  AgentValidationReport,
} from '@tracon/client';
import type {
  AgentDescriptor,
  AgentDetailResponse,
  AgentSkillDefinition,
  ModelProviderDescriptor,
  ToolDescriptor,
} from '../../lib/server-types';
import { emptyForm, fromDefinition, isValidJson, toRequest, withDefaultProvider, type FormState } from './model';

export interface UseAgentEditorResult {
  editing: boolean;
  ready: boolean;
  form: FormState;
  setForm: Dispatch<SetStateAction<FormState>>;
  tools: ReturnType<typeof useQuery<ToolDescriptor[]>>;
  skills: ReturnType<typeof useQuery<AgentSkillDefinition[]>>;
  agents: ReturnType<typeof useQuery<AgentDescriptor[]>>;
  providers: ReturnType<typeof useQuery<ModelProviderDescriptor[]>>;
  models: ModelProviderDescriptor['models'];
  request: ReturnType<typeof toRequest>;
  schemaJsonValid: boolean;
  valid: boolean;
  save: ReturnType<typeof useMutation<AgentDefinition, Error, void>>;
  validate: ReturnType<typeof useMutation<AgentValidationReport, Error, void>>;
  cancel: () => void;
}

/**
 * Owns every piece of `AgentEditorScreen` state: the form itself, the
 * catalogue queries a section needs, and the save/validate mutations.
 *
 * Sections never call the API directly — they read from and write through
 * this hook's return value, which is what makes them renderable in isolation
 * in a component test.
 */
export function useAgentEditor(name: string | undefined): UseAgentEditorResult {
  const editing = name !== undefined && name.length > 0;
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [form, setForm] = useState<FormState>(emptyForm);
  const [ready, setReady] = useState(!editing);

  const tools = useQuery({
    queryKey: ['tools'],
    queryFn: () => unwrap(client.GET('/api/tools')) as Promise<ToolDescriptor[]>,
  });
  const skills = useQuery({
    queryKey: ['skills'],
    queryFn: () => unwrap(client.GET('/api/skills')) as Promise<AgentSkillDefinition[]>,
  });
  const agents = useQuery({
    queryKey: ['agents'],
    queryFn: () => unwrap(client.GET('/api/agents')) as Promise<AgentDescriptor[]>,
  });
  const providers = useQuery({
    queryKey: ['models'],
    queryFn: () => unwrap(client.GET('/api/models')) as Promise<ModelProviderDescriptor[]>,
  });

  const existing = useQuery({
    queryKey: ['agent', name],
    queryFn: () =>
      unwrap(
        client.GET('/api/agents/{name}', { params: { path: { name: name as string } } }),
      ) as Promise<AgentDetailResponse>,
    enabled: editing,
  });

  useEffect(() => {
    if (!editing || !existing.isSuccess || ready) {
      return;
    }

    const definition = existing.data.definition;

    if (definition === null) {
      // Code-defined agents have no persisted definition to load — but the
      // catalog descriptor still knows the name/provider/model. Leaving the
      // form at `emptyForm` left `Ad` blank AND read-only (`editing` is true):
      // nothing the user could do would ever make `valid` true, so "Validate"/
      // "Save new version" stayed permanently disabled and the 409 the server
      // would answer with was never reachable (HATA-S4-010).
      const descriptor = existing.data.descriptor;

      setForm((current) => ({
        ...current,
        name: descriptor.name,
        displayName: descriptor.displayName ?? '',
        description: descriptor.description ?? '',
        provider: descriptor.model?.provider ?? current.provider,
        model: descriptor.model?.model ?? current.model,
        toolNames: [...descriptor.toolNames],
        skillNames: [...descriptor.skillNames],
        callableAgentNames: [...descriptor.callableAgentNames],
      }));
      setReady(true);

      return;
    }

    setForm(fromDefinition(definition));
    setReady(true);
  }, [editing, existing.isSuccess, existing.data, ready]);

  // A provider must be chosen before a definition can compile. Defaulting to
  // the only registered provider removes a step that has one correct answer.
  useEffect(() => {
    if (!providers.isSuccess || providers.data.length === 0) {
      return;
    }

    const providerNames = providers.data.map((provider) => provider.name);

    setForm((current) => withDefaultProvider(current, providerNames));
  }, [providers.isSuccess, providers.data]);

  const request = useMemo(() => toRequest(form), [form]);

  const save = useMutation({
    mutationFn: () =>
      (editing
        ? unwrap(
            client.PUT('/api/agents/{name}', {
              params: { path: { name: name as string } },
              body: request,
            }),
          )
        : unwrap(client.POST('/api/agents', { body: request }))) as Promise<AgentDefinition>,
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ['agents'] });
      await queryClient.invalidateQueries({ queryKey: ['agent', saved.name] });
      await queryClient.invalidateQueries({ queryKey: ['agent-versions', saved.name] });
      navigate(`agents/${encodeURIComponent(saved.name)}`);
    },
  });

  // Validation never writes anything — no query is invalidated on success.
  const validate = useMutation({
    mutationFn: () => unwrap(client.POST('/api/agents/validate', { body: request })) as Promise<AgentValidationReport>,
  });

  const models = providers.data?.find((provider) => provider.name === form.provider)?.models ?? [];
  const schemaJsonValid = form.responseFormatKind !== 'JsonSchema' || isValidJson(form.responseFormatSchema);
  const valid =
    request.name.length > 0 &&
    request.model.provider.length > 0 &&
    request.model.model.length > 0 &&
    schemaJsonValid;

  const cancel = (): void => navigate(editing ? `agents/${encodeURIComponent(name as string)}` : 'agents');

  return {
    editing,
    ready,
    form,
    setForm,
    tools,
    skills,
    agents,
    providers,
    models,
    request,
    schemaJsonValid,
    valid,
    save,
    validate,
    cancel,
  };
}
