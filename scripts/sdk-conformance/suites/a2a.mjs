// Calls the A2A endpoint with the official A2A JavaScript SDK: card discovery, a message, and the errors of the
// protocol. The SDK chooses the transport from the card and maps a JSON-RPC error to an exception, so both decisions are
// made by the SDK here and not by a hand-built request.

import { Role } from '@a2a-js/sdk';
import { ClientFactory, DefaultAgentCardResolver } from '@a2a-js/sdk/client';

import { sendDemoRole } from '../lib/demo-role.mjs';

const AGENT = 'summarizer';

const CARD_CLIENT_CHECK = 'a client built from the card the endpoint publishes can send a message';

function userMessage(text) {
  return {
    tenant: '',
    message: {
      messageId: `a2a-js-${Math.random().toString(16).slice(2)}`,
      contextId: '',
      taskId: '',
      role: Role.ROLE_USER,
      parts: [{ content: { $case: 'text', value: text } }],
      metadata: undefined,
      extensions: [],
      referenceTaskIds: [],
    },
    configuration: undefined,
    metadata: undefined,
  };
}

export async function run({ baseUrl }) {
  sendDemoRole(baseUrl);

  const results = [];
  const check = (id, ok, detail) => results.push({ id, ok, detail });
  const fail = (error) => `${error?.name}: ${String(error?.message).slice(0, 240)}`;

  const cardUrl = `${baseUrl}/tracon/a2a/${AGENT}/.well-known/agent-card.json`;
  const factory = new ClientFactory();
  let card = null;

  try {
    card = await new DefaultAgentCardResolver().resolve(cardUrl, '');
    check('the SDK resolves the published card', card.name === AGENT, `name=${card.name}`);
  } catch (error) {
    check('the SDK resolves the published card', false, fail(error));

    return { results };
  }

  const published = card.supportedInterfaces ?? [];

  check('the card declares one JSON-RPC interface', published.length === 1 && published[0].protocolBinding === 'JSONRPC', JSON.stringify(published));

  // The SDK builds its transport from the interface address as written, so the address has to be an absolute one.
  check('the interface address in the card is absolute', published.every((entry) => URL.canParse(entry.url)), JSON.stringify(published.map((entry) => entry.url)));

  // What the card says is what a client uses: a client built from the card as it is published is the one a real caller runs.
  let client = null;

  try {
    client = await factory.createFromUrl(cardUrl, '');

    await client.sendMessage(userMessage('hello from the card as published'));
    check(CARD_CLIENT_CHECK, true, null);
  } catch (error) {
    check(CARD_CLIENT_CHECK, false, fail(error));

    return { results };
  }

  try {
    const reply = await client.sendMessage(userMessage('hello from the A2A JavaScript SDK'));
    const message = reply.payload?.$case === 'message' ? reply.payload.value : reply;
    const text = (message.parts ?? []).map((part) => part.content?.value ?? '').join('');

    check('a message is answered with the agent reply', text.includes('hello from the A2A JavaScript SDK'), JSON.stringify(reply).slice(0, 240));
  } catch (error) {
    check('a message is answered with the agent reply', false, fail(error));
  }

  // A task that does not exist is the protocol's own error, which the SDK raises as an exception with the code.
  let notFound = null;

  try {
    await client.getTask({ tenant: '', id: 'no-such-task', historyLength: undefined });
  } catch (error) {
    notFound = error;
  }

  check('an unknown task is a protocol error, not a resolved value', notFound !== null, notFound ? fail(notFound) : 'resolved');

  return { results };
}
