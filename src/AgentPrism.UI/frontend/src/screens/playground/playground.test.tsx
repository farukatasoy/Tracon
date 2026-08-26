import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock, sseFixture, sseFrame, sseResponse } from '../../test/api-fixtures';
import { renderScreen, screen, waitFor } from '../../test/render';
import { PlaygroundScreen } from '../playground';

const agents = [{ name: 'support', displayName: 'Support' }];
const agentDetail = { descriptor: { name: 'support', origin: 'Code', sourceName: 'code' }, definition: null };
const newConversation = { id: 'session-1', object: 'conversation', created_at: 0, metadata: null };

function baseOverrides() {
  return [
    fixture('GET', 'api/agents', agents),
    fixture('GET', 'api/agents/:name', agentDetail),
    fixture('POST', 'v1/conversations', newConversation),
  ];
}

describe('PlaygroundScreen', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  it('streams a reply and links it to the recorded run', async () => {
    restoreFetch = installApiMock([
      ...baseOverrides(),
      sseFixture(
        'POST',
        'api/agents/:name/run',
        sseFrame('run', { runId: 'run-1' }) +
          sseFrame('update', { contents: [{ $type: 'text', text: 'Hello there' }] }) +
          sseFrame('done', {}),
      ),
    ]);

    const user = userEvent.setup();

    renderScreen(<PlaygroundScreen />);

    const input = await screen.findByTestId('playground-input');

    await user.type(input, 'Hi');
    await user.click(screen.getByTestId('playground-send'));

    expect(await screen.findByText('Hello there')).toBeTruthy();
    expect(await screen.findByText(/run run-1/)).toBeTruthy();
  });

  it('shows the failure a stream error frame carries', async () => {
    restoreFetch = installApiMock([
      ...baseOverrides(),
      sseFixture(
        'POST',
        'api/agents/:name/run',
        sseFrame('run', { runId: 'run-1' }) + sseFrame('error', { type: 'ProviderError', message: 'model unavailable' }),
      ),
    ]);

    const user = userEvent.setup();

    renderScreen(<PlaygroundScreen />);

    await user.type(await screen.findByTestId('playground-input'), 'Hi');
    await user.click(screen.getByTestId('playground-send'));

    expect(await screen.findByText(/ProviderError: model unavailable/)).toBeTruthy();
  });

  it('answers a pending approval', async () => {
    // The first run asks for approval; the decision that follows it (a
    // SEPARATE run, same session — MAF ends a run at an approval request and
    // expects the answer as the next call) gets a plain reply. A fixture
    // that replayed the same script for both would show a second, fresh
    // approval card forever — a fixture bug, not the finding this covers.
    let calls = 0;
    const run: ReturnType<typeof fixture> = {
      method: 'POST',
      pattern: 'api/agents/:name/run',
      handler: () => {
        calls += 1;

        return sseResponse(
          calls === 1
            ? sseFrame('run', { runId: 'run-1' }) +
                sseFrame('update', { contents: [{ requestId: 'req-1', toolCall: { name: 'deleteOrder', arguments: {} } }] }) +
                sseFrame('done', {})
            : sseFrame('run', { runId: 'run-2' }) +
                sseFrame('update', { contents: [{ $type: 'text', text: 'Deleted.' }] }) +
                sseFrame('done', {}),
        );
      },
    };

    restoreFetch = installApiMock([...baseOverrides(), run]);

    const user = userEvent.setup();

    renderScreen(<PlaygroundScreen />);

    await user.type(await screen.findByTestId('playground-input'), 'Delete order 9');
    await user.click(screen.getByTestId('playground-send'));

    const approve = await screen.findByTestId('approval-approve');

    await user.click(approve);

    // The card marks itself decided immediately (`decide()`), before the
    // follow-up run's own frames arrive — that immediate feedback is what
    // stops a double-click from sending the same decision twice.
    await waitFor(() => {
      expect(screen.queryByTestId('approval-approve')).toBeNull();
    });

    expect(await screen.findByText('Deleted.')).toBeTruthy();
  });

  it('adds and then clears a pending attachment', async () => {
    restoreFetch = installApiMock([
      ...baseOverrides(),
      fixture('POST', 'api/attachments', {
        id: 'att-1',
        fileName: 'note.txt',
        mediaType: 'text/plain',
        byteSize: 4,
      }),
    ]);

    const user = userEvent.setup();

    renderScreen(<PlaygroundScreen />);
    await screen.findByTestId('playground-input');

    const file = new File(['note'], 'note.txt', { type: 'text/plain' });

    await user.upload(screen.getByTestId('attachment-input'), file);

    const chip = await screen.findByTestId('attachment-chip');

    expect(chip.textContent).toContain('note.txt');

    await user.click(screen.getByRole('button', { name: /remove note\.txt/i }));

    await waitFor(() => {
      expect(screen.queryByTestId('attachment-chip')).toBeNull();
    });
  });

  it('renders prior messages for a session resumed from the URL', async () => {
    window.history.pushState(null, '', '/?sessionId=session-9');

    restoreFetch = installApiMock([
      ...baseOverrides(),
      fixture('GET', 'api/sessions/:sessionId', {
        id: 'session-9',
        agentName: 'support',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        messages: [{ messageId: 'm1', role: 'user', authorName: null, contents: [{ $type: 'text', text: 'Earlier question' }] }],
        state: null,
      }),
    ]);

    renderScreen(<PlaygroundScreen />);

    expect(await screen.findByText('Prior messages')).toBeTruthy();
    expect(await screen.findByText('Earlier question')).toBeTruthy();

    window.history.pushState(null, '', '/');
  });
});
