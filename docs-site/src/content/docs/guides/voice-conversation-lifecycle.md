---
title: Voice conversation lifecycle
description: Who may start a voice conversation, how a browser with its own sign-in opens one, how long it lasts and how it ends.
---

A [voice conversation](/guides/voice/#enable-live-conversation) is a long-lived socket that runs
an agent turn after turn. This page covers what happens around it: the questions asked before
it may run, how a host with its own sign-in authenticates it, the time limit of one connection,
how an agent ends it, and how it closes. The frames themselves are in the
[protocol reference](/reference/voice-conversation-protocol/).

The examples use `UseVoice()` from the `Tracon.Voice` package, which the
[voice guide](/guides/voice/) installs.

## A conversation is a run start

```mermaid
sequenceDiagram
    accTitle: Where a voice conversation asks whether a run may start
    accDescr: The handshake asks the session question before the agent is known. When the start frame names the agent, the server asks the run questions, then calls the host's resolver and answers ready. Before every turn it asks them again, and a refusal is an error frame.
    participant C as Client
    participant S as Server
    participant H as Run questions
    C->>S: open the socket
    S->>S: session question (SessionAccess.Voice)
    C->>S: start with the agent
    S->>H: may this caller run this agent in this session?
    H-->>S: allowed or refused
    S->>S: the host's resolver, only if allowed
    S-->>C: ready, or error and the conversation ends
    loop every turn
        C->>S: audio and commit
        S->>H: asked again, before any audio is transcribed
        H-->>S: allowed or refused
        S-->>C: the answer, or error and then done
    end
```

The question above is about the session, and it is asked before the agent is known: the
`start` frame names the agent after the socket is open. So the conversation asks again, with
the three questions every other way to start a run asks:

1. The registered `IRunAuthorizationHandler` is asked `AuthorizeRunAsync` with
   `RunAccess.Start`, the agent, the session, the tenant and the user.
2. The session's owner is checked when session ownership is on.
3. The tenant's quota is checked.

They are asked when `start` arrives, before `ResolveConnection`, and before every turn,
including the opening prompt of a conversation that has one. A greeting is not a run and
asks nothing more. A caller who may open a conversation therefore cannot name another agent
in it and run it.

| Refused when | What the client reads | What follows |
|---|---|---|
| `start` | `error` with the reason the handler or the quota gave, the text an HTTP run would answer | The conversation ends and the record's end reason is `Error`. The host's resolver is not called |
| A turn | `error`, then `done` with the number of turns already completed | The turn is not counted, its audio is never sent to the transcription provider and no run starts. The conversation goes on, and the next turn is asked again |

A handler that throws, or a session store that throws while the owner is checked, refuses
with the fixed text `The run authorization check failed. Retry the request.`; the exception
stays in your log. A quota store that throws follows `Tracon:Quotas:AllowOnStoreFailure`, as
it does for an HTTP run: the turn runs by default, and with the setting off it is refused with
`Quota could not be verified.`

A spent quota refuses a new conversation and the next turn of an open one, and never cuts a
turn in progress. A voice turn was recorded and counted before, but never refused. The quota
counts the runs that have ended, so conversations that start together can pass it together,
and a slow handler or store delays the turn it is asked about.

## Carry your own token from the browser

Tracon's `tracon.token.` subprotocol carries a credential **Tracon** checks: its static
`AuthToken` or one of its API keys. A host that has its own sign-in (a JWT it issued) puts
that token in a subprotocol **of its own**, and its own authentication reads it from the
`Sec-WebSocket-Protocol` header. Tracon reads only its own prefix and ignores every other
offer, so it selects `tracon.voice.v1` and never echoes the token.

On the host, give the endpoints the policy your sign-in satisfies and let the sign-in find
the token in the header. With the JWT bearer handler that is its `OnMessageReceived` event:
set `context.Token` to the value `ReadHostToken` returns. The helper reads the value your
prefix marks:

```csharp
using Microsoft.AspNetCore.Http;

// "host.bearer." is your choice of prefix. Do not use "tracon.token.": Tracon reads that prefix as its own credential
// and refuses a value that is not one.
static string? ReadHostToken(HttpRequest request)
{
    const string prefix = "host.bearer.";

    foreach (var header in request.Headers.SecWebSocketProtocol)
    {
        foreach (var offer in (header ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (offer.StartsWith(prefix, StringComparison.Ordinal))
            {
                return offer[prefix.Length..];
            }
        }
    }

    return null;
}

app.MapTracon("/tracon", options => options.RequireAuthorization("HostAccess"));
```

In the browser, offer it next to the protocol with the `subProtocols` option of
[`createVoiceConversation`](/guides/typescript-client/#talk-to-a-voice-conversation), or in
the second argument of your own `new WebSocket(url, [protocol, 'host.bearer.' + jwt])`:

```ts
createVoiceConversation({ baseUrl, sessionId, agent, subProtocols: () => ['host.bearer.' + currentJwt()] });
```

OpenIddict validation has no `OnMessageReceived`. Add an event handler for its
`ProcessAuthenticationContext` event with `AddValidation(...)`, ordered just after
`ExtractAccessTokenFromQueryString`, and set `context.AccessToken` to the value
`ReadHostToken` returns. Reject the request there (`context.Reject`) when it also carries a
token in the query string or an `Authorization` header, or two values with your prefix, so one
request carries one credential.

These limits apply:

- A subprotocol value holds only the characters of an HTTP token. A base64url JWT does
  (`A-Z a-z 0-9 - _ .`); a value with `=`, `/` or a space does not, so encode it first.
- The `Sec-WebSocket-Protocol` header is a header like the others to your proxy, your APM
  and your HTTP logging: most redaction lists name `Authorization` and not this header, so
  add it. A proxy also caps one header line (8 KB is a common limit), and a JWT with many
  claims can pass it; the browser then reports only a failed handshake.
- Do not put the token in the query string. It is the one place the address, your proxy's
  log and the browser's history all keep it. Tracon refuses its own token there for that reason, and the same
  holds for yours.
- Tracon's own checks still apply to a socket your sign-in authenticated. Tracon cannot tell
  a token the page holds from an identity the browser sends by itself (a cookie), so a page
  of another site is refused unless its origin is in `AllowedOrigins`, a client with no
  `Origin` header is not judged by it, and the run questions above are asked as for any
  other conversation.

## Limit how long a conversation lasts

`MaxConnectionDuration` (30 minutes by default) is one limit for every connection. It is a
hygiene limit: when it passes, the server closes the socket without a word and the client is
expected to reconnect, and the session keeps its history.

A host whose conversations differ in length (a kind of conversation, a tenant's plan) sets a
shorter limit for each connection with `MaxDuration`, in the resolver:

```csharp
builder.AddTracon()
    .UseVoice(builder.Configuration.GetSection(VoiceOptions.SectionName))
    .UseVoiceConversation(options =>
    {
        options.MaxConnectionDuration = TimeSpan.FromMinutes(30);
        options.ResolveConnection = (request, cancellationToken) =>
            ValueTask.FromResult<VoiceConnectionSettings?>(new VoiceConnectionSettings
            {
                MaxDuration = request.AgentName == "assessment" ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(15),
            });
    });
```

The value must be greater than zero and no greater than `MaxConnectionDuration`, which stays
the ceiling of every connection: a resolver that returns anything else fails like any other
mistake of the resolver, and the limit is never cut or stretched to fit. The time is counted
from the moment the connection opened.

When the limit is reached the turn in progress is cut off (its `done` carries
`"cancelled": true`), the server sends `ended` with `"reason": "duration"` and closes the
socket normally, and the record's end reason is `DurationLimit`. **The client does not
reconnect:** the conversation is over. That is what tells this limit from the global one,
whose close carries no `ended` frame.

Nothing remembers how long an earlier connection of the same session lasted. A host that
allows a conversation a fixed total sums the time already used and returns what is left each
time it is asked, or refuses the new connection in the resolver.

### How long a person may stay silent

`IdleTimeout` (2 minutes by default) closes a connection that sends no frame for that long. A
host whose conversations differ in how long a person may think between turns (a long interview
and a quick question) sets a different value for each connection with `IdleTimeout` in the
resolver, next to `MaxDuration`:

```csharp
builder.AddTracon()
    .UseVoiceConversation(options =>
    {
        options.ResolveConnection = (request, cancellationToken) =>
            ValueTask.FromResult<VoiceConnectionSettings?>(new VoiceConnectionSettings
            {
                IdleTimeout = request.AgentName == "interview" ? TimeSpan.FromSeconds(90) : null,
            });
    });
```

The rule is `MaxDuration`'s: the value must be greater than zero and no greater than the
`IdleTimeout` of the conversation options, which stays the ceiling, and anything else fails
like any other mistake of the resolver. The close is the same as for the global limit
(`The connection stayed idle.`, record end reason `IdleTimeout`). A client that must keep a
quiet line open sends any frame, a silent audio frame for instance, inside the limit.

## Let the agent end the conversation

An agent that lists the tool `end_conversation` in its `ToolNames` can end the conversation
after its answer. It takes no argument. Tell the agent when to use it in its instructions
(the topic is done, the time is up, the caller said goodbye) and to say a short goodbye in
the same answer:

```csharp
tracon.AddAgent(new AgentDefinition
{
    Name = "coach",
    Instructions = "Coach the student. When the exercise is done, say goodbye and call end_conversation.",
    Model = new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "your-model" },
    ToolNames = ["end_conversation"],
});
```

The server lets the answer finish, speaks it, sends `done`, then `ended` with
`"reason": "agent"`, and closes the socket normally. The record's end reason is `Agent`.
If the user interrupts the goodbye (`cancel`), or the turn fails, the request is dropped and
the conversation goes on. An agent that does not list the tool can never end a conversation,
and the tool ends nothing when an agent runs outside a voice conversation. Every close has
its own frame, description and end reason, [below](#how-a-conversation-closes).

## What the agent says before a tool call

An agent can say "one moment" before a lookup if its instructions tell it to. The words reach the
caller **while the tool runs**: when the model's answer contains a tool call, the text it has
produced so far is spoken before the tool starts, and the text after the tool is a sentence of its
own. A sentence that ends the answer before the call needs no full stop to be heard.

## Keep a batch from queueing a live turn

The built-in speech client holds one concurrency pool (`MaxConcurrentRequests`, 2 by default) for synthesis, dialogue and
transcription together. A batch that voices a podcast takes the slots a live conversation's turn waits for. Give the batch a
**second built-in client** with a pool of its own and call it by name; the conversation keeps the default. The second client
starts from the options of `UseVoice`; to the tenant's egress policy, its own key and the price it is still `elevenlabs`:

```csharp
builder.AddTracon()
    .UseVoice(builder.Configuration.GetSection(VoiceOptions.SectionName))
    .AddElevenLabsSynthesizer("batch", options => options.MaxConcurrentRequests = 4);
```

A failed call of the built-in client throws a `ProviderInvocationException`
([run errors](/concepts/run-errors/#decide-a-retry-from-the-failure-not-from-its-text)).

## How a conversation closes

Every close the server starts is a normal closure with a description, and the record carries
the end reason. What came before the close tells the client whether to come back:

| Cause | Frame before the close | Close description | End reason |
|---|---|---|---|
| The client sent `stop` or closed the socket | none | `The conversation closed.` | `Client` |
| The agent ended it | `ended` (`agent`) | `The conversation was ended by the agent.` | `Agent` |
| The limit the host set for the connection | `ended` (`duration`) | `The conversation reached its time limit.` | `DurationLimit` |
| `MaxConnectionDuration`, the global limit | none | `The connection reached the duration limit.` | `DurationLimit` |
| No frame from the client for `IdleTimeout` | none | `The connection stayed idle.` | `IdleTimeout` |
| The host is stopping | none | `The server is shutting down.` | `ServerShutdown` |
| The host's resolver failed or refused, or the run questions refused the `start` | `error` | `The conversation closed with an error.` | `Error` |
| Any other failed `start` (no such agent, an unknown audio format, a second `start`) | `error` | `The conversation closed.` | `Client` |
| A turn failed | `error`, then `done` | none: the conversation goes on | none |

The end reason is decided once, by the first thing that ended the conversation, and a client
that drops the connection the moment it reads `ended` does not change it. `ServerShutdown` is
written only when the host is stopping: a client that goes away while the host runs is
`Client`. A host that stops closes its open conversations and cuts the turn in progress.

A close is a handshake: the server sends its close frame and waits for the client's. A
browser answers by itself; another client must keep reading until the close arrives. The
server waits five seconds, and then drops the connection, so a client that reads nothing
cannot hold a tenant's connection slot.

## Read next

- [Voice and live conversation](/guides/voice/) — enable the layer, decide the voice on the server, open and end the conversation.
- [Voice conversation protocol](/reference/voice-conversation-protocol/) — every frame, field, handshake answer and close.
- [Run and session authorization](/guides/embedding/#6--run-and-session-authorization) — the handler these questions ask.
