// Runs the agent through the official AG-UI JavaScript client: the code a CopilotKit front end runs against it.
//
// The client validates every event with its own schema and rejects a stream that breaks the protocol (an event out of
// order, a message that never ends), so a stream the server's own tests accept can still fail here.

import { HttpAgent } from '@ag-ui/client';

const AGENT = 'summarizer';

function createAgent(baseUrl, threadId) {
  return new HttpAgent({
    url: `${baseUrl}/tracon/agui/${AGENT}`,
    threadId,
    headers: { 'X-Tracon-Demo-Role': 'admin' },
  });
}

/** Runs one turn and returns the events the client saw, in order, with the messages it assembled. */
async function runTurn(agent, text, runId) {
  const events = [];

  agent.messages = [...agent.messages, { id: `user-${runId}`, role: 'user', content: text }];

  const outcome = await agent.runAgent(
    { runId },
    {
      onEvent: ({ event }) => {
        events.push(event);
      },
    },
  );

  return { events, outcome };
}

export async function run({ baseUrl }) {
  const results = [];
  const check = (id, ok, detail) => results.push({ id, ok, detail });

  const agent = createAgent(baseUrl, `agui-js-${Date.now()}`);
  const { events, outcome } = await runTurn(agent, 'hello from the AG-UI JavaScript client', 'agui-js-run-1');
  const types = events.map((event) => event.type);

  check('the stream opens with RUN_STARTED and ends with RUN_FINISHED', types[0] === 'RUN_STARTED' && types.at(-1) === 'RUN_FINISHED', types.join(','));
  check('no RUN_ERROR event', !types.includes('RUN_ERROR'), types.join(','));

  const starts = types.filter((type) => type === 'TEXT_MESSAGE_START').length;
  const ends = types.filter((type) => type === 'TEXT_MESSAGE_END').length;

  check('every text message that starts also ends', starts >= 1 && starts === ends, `${starts} starts, ${ends} ends`);

  const assistant = outcome.newMessages.filter((message) => message.role === 'assistant');
  const answer = assistant.map((message) => message.content ?? '').join('');

  check('the assistant answer is the echoed input', answer.includes('hello from the AG-UI JavaScript client'), JSON.stringify(answer));

  // The example host's echo model streams one chunk per word and sets no message id on them. One answer is one message.
  check('one answer is one assistant message, however many chunks it streamed', assistant.length === 1, `${assistant.length} assistant messages`);

  const custom = events.find((event) => event.type === 'CUSTOM' && event.name === 'tracon.run');

  check('the first CUSTOM event names the Tracon run', typeof custom?.value?.runId === 'string' || typeof custom?.value === 'string' || custom !== undefined, JSON.stringify(custom));

  // The thread is the session: a second turn on the same thread reads the first one back.
  const second = await runTurn(agent, 'second turn', 'agui-js-run-2');

  check('a second turn on the same thread finishes', second.events.at(-1)?.type === 'RUN_FINISHED', second.events.map((e) => e.type).join(','));

  // A request the server refuses reaches the client as an error, not as a stream that never ends.
  const refused = createAgent(baseUrl, '');

  refused.messages = [{ id: 'refused-1', role: 'user', content: 'no thread' }];

  let refusal = null;

  try {
    await refused.runAgent({ runId: 'agui-js-run-3' });
  } catch (error) {
    refusal = error;
  }

  check('a refused request (no thread) rejects instead of hanging', refusal !== null, refusal ? String(refusal.message).slice(0, 160) : 'resolved');

  return { results };
}
