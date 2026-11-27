---
title: Google settings
description: The Gemini thinking depth, how ReasoningEffort maps to it, how thinking tokens are counted, the safety thresholds and how a Gemini error reaches your code.
---

Google is a provider like the others: [register it and bind an agent](/guides/model-providers/), and the agent
reads its credential from the host. This page covers what is specific to Gemini and set through
`ModelBinding.ProviderSettings`, the portable `ReasoningEffort`, or read from a run.

Google supports five `google.safety.*` thresholds plus
`google.thinking.budgetTokens`, `google.thinking.level` and `google.thinking.includeThoughts`.

| Key | Values | Sent as |
|---|---|---|
| `google.thinking.level` | `minimal`, `low`, `medium`, `high` (any case) | `thinkingConfig.thinkingLevel` |
| `google.thinking.budgetTokens` | integer `-1..65535`; `-1` lets the model decide and `0` disables thinking | `thinkingConfig.thinkingBudget` |
| `google.thinking.includeThoughts` | `true`, `false` | `thinkingConfig.includeThoughts` |

- **Level or budget, not both.** They are two controls for the depth of thinking. Google
  recommends the level for the Gemini 3 models and the budget is what earlier models take.
  Setting both is an error when the agent is built, and so is a level outside the four.
  A model that does not offer a level can reject the request.
- **`ReasoningEffort` on Gemini.** `ModelBinding.ReasoningEffort` becomes a thinking level:
  `Low` is `LOW`, `Medium` is `MEDIUM`, and `High` and `ExtraHigh` are both `HIGH`; `None` becomes a thinking
  budget of zero. When `google.thinking.level` or `google.thinking.budgetTokens` is set, the provider
  setting wins and `ReasoningEffort` is not sent, so a request never carries a level next to a budget.
  `google.thinking.includeThoughts` does not choose a depth and leaves the effort in place.
- **Thinking tokens count as output.** Gemini reports the answer and the thinking as two
  separate counts and bills both at the output price. A run records `outputTokens` as the sum, as it does for
  OpenAI and Anthropic, and `reasoningTokens` as the thinking part of it, so the output cost is the output
  tokens times the output price and `reasoningTokens` never exceeds `outputTokens`.

The `google.safety.*` thresholds take `BLOCK_LOW_AND_ABOVE`, `BLOCK_MEDIUM_AND_ABOVE`,
`BLOCK_ONLY_HIGH`, `BLOCK_NONE` or `OFF`. A safety-filtered empty response becomes a
failed run with error type `content_filtered`; one whose final turn already has text completes with a
`tracon.output.filtered` run event.
Gemini answers a non-streamed call that its filter stops with no text at all, so on that path a filtered
answer is always the empty case and a run fails with `content_filtered`; only a streamed call can send text
before the filter stops it and complete with the event. A turn that Gemini ends because the model wrote a
function call it could not use (finish reason `MALFORMED_FUNCTION_CALL`, or `UNEXPECTED_TOOL_CALL` when the
request declared no such tool) and that holds no call fails the run as a provider error
(`upstream_error`); it does not complete with an empty answer, and a fallback chain goes on to the next model.
An error answer from Gemini to a chat call or an image generation carries its HTTP status like every other
provider's: a `503` or a `429` is transient and moves a call to the next model of a `Fallbacks` chain, and a
`400` is neither retried nor counted against the circuit of a shared key. Speech synthesis is not translated: its
failure is a `TraconException` that names the status in its message. See
[Decide a retry from the failure](/concepts/run-errors/#decide-a-retry-from-the-failure-not-from-its-text).

## Read next

- [Model providers](/guides/model-providers/) — register a provider, bind an agent and set the portable fields.
- [Gemini speech and dialogue](/guides/gemini-speech/) — the Gemini speech synthesizer.
- [Run errors](/concepts/run-errors/) — how a failure is classified and which ones move a call to the next model.
