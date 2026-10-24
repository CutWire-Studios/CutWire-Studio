# Hosted AI endpoint

`POST /api/v1/forge/ai`, called from `src/services/account/hosted.ts`. The Bun implementation it replaces
was `server/ai.ts` in CutWire-Tinker; the .NET port must match this contract.

## Request

`Content-Type: application/json`, cookie session (`credentials: include`), header `X-Requested-With: cutwire`.
Body limit 256 KiB (longer: 413).

```json
{ "prompt": "string", "history": [{ "role": "user" | "assistant", "text": "string" }], "doc": ForgeDoc }
```

- `prompt`: trimmed, truncated to 1500 characters, required (empty: 400).
- `history`: only entries with `role` of `user` or `assistant` and a string `text`; last 8 kept, each `text` truncated to 1500. Anything else is dropped. Clients cannot send system prompts, tools or tool results.
- `doc`: a ForgeDoc, validated with `parseForgeDoc` (invalid: 400 with the validation message). More than 150 nodes: 413. The client blanks asset `data` and sends only `preview.thumbTime`; it restores both from its local copy on every document that comes back.

## Gates, in order

| Status | Body | Condition |
| --- | --- | --- |
| 401 | `{ "error": "Sign in with your CutWire account to use the built-in AI." }` | no session |
| 429 | `{ "error": "Slow down a little: too many requests in the last minute." }` | per-user limiter, 8 requests per 60 s |
| 413 | `{ "error": "This effect is too large for the built-in AI." }` | body over 256 KiB |
| 400 | `{ "error": "Bad request." }` | body is not JSON |
| 400 | `{ "error": "Say what you'd like to make." }` | empty prompt |
| 400 | `{ "error": <doc validation message> }` | invalid doc |
| 413 | `{ "error": "This graph is too big for the built-in AI." }` | over 150 nodes |
| 429 | `{ "error": "The built-in AI has used today's free allowance. It resets at midnight UTC, or add your own AI key in settings.", "quota": Quota }` | user or global daily neuron budget used up |

The client reads `error` and optional `quota` from any non-OK JSON body; a 401 marks the user signed out.

## Success: streamed NDJSON

`200`, `Content-Type: application/x-ndjson; charset=utf-8`, `Cache-Control: no-store`, `X-Accel-Buffering: no`.
One JSON event per line (`\n` terminated), flushed as produced:

- agent events (`AgentEvent` from `@cutwire/forge-core/ai/agent`): reasoning, tool progress, `pending`, etc. Reasoning is capped at 2000 characters.
- `{ "type": "progress", "doc": ForgeDoc }`: the graph after each agent step.
- `{ "type": "done", "doc": ForgeDoc, "reply": string, "quota": Quota, "stopped"?: string }`: final event; `reply` truncated to 600 characters. `stopped` is set when the run ended early (e.g. budget exhausted: "I ran out of today's free AI allowance, so I stopped here. Everything I did so far is kept.").
- `{ "type": "error", "error": "The AI couldn't finish that. Try again in a moment.", "quota"?: Quota }`: terminal failure (details are logged, not sent).

```ts
type Quota = { userLeft: number; userLimit: number; globalLeft: number }
```

`userLeft = max(0, floor(userDailyNeurons - used))`, `globalLeft = max(0, floor(dailyNeurons - used))`, `userLimit = userDailyNeurons`.

## Agent loop and limits

- Model: Workers AI `@cf/zai-org/glm-4.7-flash` (via AI Gateway when configured), OpenAI-format chat completions, `tool_choice: auto`, `temperature: 0.2`, `max_completion_tokens: 2048`, `stream: true` with `include_usage`, 90 s timeout per call.
- At most 20 agent steps per request; text limit 600, thinking limit 2000.
- System prompt, tools and tool execution are server side (`runAgent` plus the BUILD_TOOLS in forge-core).
- Budget (neurons): defaults 9000 per UTC day globally, 600 per user per UTC day; 5.5 neurons per 1000 input tokens, 36.364 per 1000 output tokens.
  Before each model call, reserve an upper-bound estimate (`ceil((json(messages).length + 14000) / 3)` input tokens plus the full 2048 output allowance); if the reservation fails the run stops with the `stopped` message above. After the call, settle the difference using the reported neurons, else priced token usage, else keep the reservation.

## Account

The old `GET /api/me` also returned the quota. The client now gets identity from `GET /api/v1/me` and only learns the quota from this endpoint's responses.
