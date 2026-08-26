import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock } from '../../test/api-fixtures';
import { fireEvent, renderScreen, screen, waitFor } from '../../test/render';
import { AgentEditorScreen } from '../agent-editor';

const providers = [
  { name: 'anthropic', displayName: 'Anthropic', status: 'Unknown', models: [{ name: 'claude', contextWindowTokens: null }] },
  { name: 'openai', displayName: 'OpenAI', status: 'Unknown', models: [] },
];

const fullDefinition = {
  name: 'support',
  displayName: 'Support',
  description: 'Answers support questions.',
  instructions: 'Be brief.',
  instructionsByCulture: {},
  model: { provider: 'anthropic', model: 'claude', temperature: null, maxOutputTokens: null, topP: null, reasoningEffort: null, responseFormat: null, fallbacks: [] },
  toolNames: [],
  skillNames: [],
  callableAgentNames: [],
  harness: null,
  compaction: null,
  memory: null,
  origin: 'Database',
  version: 1,
};

/** Empty catalogue lists for every section — enough for the screen to settle without a section-specific override. */
function baseOverrides() {
  return [
    fixture('GET', 'api/tools', []),
    fixture('GET', 'api/skills', []),
    fixture('GET', 'api/agents', []),
    fixture('GET', 'api/models', providers),
  ];
}

describe('AgentEditorScreen', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  it('loads an existing definition into the form', async () => {
    restoreFetch = installApiMock([
      ...baseOverrides(),
      fixture('GET', 'api/agents/:name', { descriptor: { name: 'support', origin: 'Database', sourceName: 'database' }, definition: fullDefinition }),
    ]);

    renderScreen(<AgentEditorScreen name="support" />);

    const nameInput = await screen.findByTestId('agent-name');

    await waitFor(() => {
      expect((nameInput as HTMLInputElement).value).toBe('support');
    });

    expect((screen.getByTestId('agent-instructions') as HTMLTextAreaElement).value).toBe('Be brief.');
    expect((screen.getByTestId('agent-model') as HTMLInputElement).value).toBe('claude');
  });

  it('defaults the provider for a new agent once the catalogue loads (HATA-S4-009)', async () => {
    restoreFetch = installApiMock(baseOverrides());

    renderScreen(<AgentEditorScreen />);

    await waitFor(() => {
      const providerSelect = screen.getByLabelText(/^Provider/) as HTMLSelectElement;

      expect(providerSelect.value).toBe('anthropic');
    });
  });

  it('flags invalid JSON in the response schema and disables Validate/Save', async () => {
    restoreFetch = installApiMock(baseOverrides());

    const user = userEvent.setup();

    renderScreen(<AgentEditorScreen />);

    await user.type(await screen.findByTestId('agent-name'), 'demo');
    await user.type(screen.getByTestId('agent-model'), 'claude');
    await user.selectOptions(screen.getByLabelText(/^Provider/), 'anthropic');
    await user.selectOptions(screen.getByTestId('agent-response-format'), 'JsonSchema');

    const schema = screen.getByTestId('agent-response-schema');

    // `userEvent.type` parses `{`/`}` as special-key syntax; a raw DOM change
    // event is the right tool for pasting literal, syntactically invalid text.
    fireEvent.change(schema, { target: { value: '{ not json' } });

    expect(screen.getByRole('alert')).toHaveProperty('textContent', 'Not valid JSON.');
    expect((screen.getByTestId('agent-validate') as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByTestId('agent-save') as HTMLButtonElement).disabled).toBe(true);
  });

  it('shows the validation report after Validate succeeds', async () => {
    restoreFetch = installApiMock([
      ...baseOverrides(),
      fixture('POST', 'api/agents/validate', { valid: true, inconclusive: false, messages: [] }),
    ]);

    const user = userEvent.setup();

    renderScreen(<AgentEditorScreen />);

    await user.type(await screen.findByTestId('agent-name'), 'demo');
    await user.type(screen.getByTestId('agent-model'), 'claude');
    await user.selectOptions(screen.getByLabelText(/^Provider/), 'anthropic');

    await user.click(screen.getByTestId('agent-validate'));

    expect(await screen.findByText('Valid')).toBeTruthy();
  });

  it('shows an error note when Save fails', async () => {
    restoreFetch = installApiMock([
      ...baseOverrides(),
      fixture('POST', 'api/agents', { title: 'Name already exists', detail: 'An agent named "demo" already exists.' }, 409),
    ]);

    const user = userEvent.setup();

    renderScreen(<AgentEditorScreen />);

    await user.type(await screen.findByTestId('agent-name'), 'demo');
    await user.type(screen.getByTestId('agent-model'), 'claude');
    await user.selectOptions(screen.getByLabelText(/^Provider/), 'anthropic');

    await user.click(screen.getByTestId('agent-save'));

    const alert = await screen.findByRole('alert');

    expect(alert.textContent).toContain('Name already exists');
  });
});
