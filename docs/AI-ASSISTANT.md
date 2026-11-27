# AI assistant and providers (ControlPhone 3.0)

ControlPhone 3.0 has a built-in assistant that is part of the application, not a separate chat page: it reads the
real state of your phones and runs the real ControlPhone functions through a tool registry.

## Providers

| Provider | Protocol | Default base URL | Example models |
|---|---|---|---|
| Google Gemini | `generativelanguage` | `https://generativelanguage.googleapis.com` | `gemini-2.5-flash`, `gemini-2.5-pro` |
| OpenAI | OpenAI | `https://api.openai.com/v1` | `gpt-4o-mini`, `gpt-4o`, `gpt-4.1` |
| Anthropic Claude | Anthropic | `https://api.anthropic.com` | `claude-sonnet-4-5`, `claude-haiku-4-5` |
| OpenRouter | OpenAI-compatible | `https://openrouter.ai/api/v1` | `anthropic/claude-sonnet-4.5` |
| DeepSeek | OpenAI-compatible | `https://api.deepseek.com/v1` | `deepseek-chat` |
| Groq | OpenAI-compatible | `https://api.groq.com/openai/v1` | `llama-3.3-70b-versatile` |
| Ollama (local) | OpenAI-compatible | `http://localhost:11434/v1` | `qwen2.5:7b`, `llama3.1:8b` |
| Custom | OpenAI-compatible | your URL | any model id |

You can add several providers and switch between them per conversation. One provider is the default for new chats.

## Adding a key

1. Click **AI** in the top bar (or ⋯ → *AI providers…*, or the gear icon inside the chat window).
2. *Add provider* → pick a preset (or *Custom* for OpenAI-compatible).
3. Fill name, base URL and model, paste your API key, then **Test connection**.
4. **Save**. Use *Default* on the provider you want for new conversations.

Notes:

- Each user enters their own key. No key is compiled into the program or stored in the repository.
- The key is encrypted with **Windows DPAPI** for the current Windows account and stored in `data/ai/ai.json`.
  Copying that file to another PC or Windows account does not reveal or reuse the key.
- Keys are never written to logs and are redacted from error messages (`?key=…`, `Bearer …`, `sk-…`).
- Keys are sent only to the provider you selected, in the request header that provider expects
  (`x-goog-api-key`, `x-api-key`, `Authorization: Bearer`).
- **Delete all AI data** (in the provider dialog) removes providers, encrypted keys and chat history.

## Per-provider options

- **Max answer length** (`max_tokens` / `maxOutputTokens`).
- **Temperature** (creativity) — clamped per protocol (Anthropic 0–1, others 0–2).
- **Timeout** in seconds (retries on 429/5xx).
- **Fetch model list** — reads the provider's `/models` endpoint when available.
- **Test connection** — sends a minimal prompt and shows the answer or the exact error.

## Assistant behaviour

The assistant receives:

- the list of phones with number, name, model, Android version, status, transport, battery, groups, root, pause and
  streaming flags;
- the current selection;
- the UI language, so it answers in the same language you write in (Arabic included);
- a guide to how ControlPhone works (used instead of guessing).

Tools available to the assistant (24):

| Tool | What it does | Approval |
|---|---|---|
| `list_devices`, `device_details`, `list_groups`, `get_settings`, `diagnostics`, `read_logs`, `app_guide` | read-only information | no |
| `select_devices`, `assign_group`, `rename_device`, `stream_control`, `screenshot`, `connect_device`, `scan_lan` | view / organisation | no |
| `device_action`, `send_text`, `open_url`, `app_control`, `install_apk`, `push_file`, `shell`, `set_setting` | changes the phones or settings | **yes** |

When approval is required, the assistant must state what it will do and on which phones; ControlPhone then shows a
confirmation dialog with the tool, the target phones and the parameters. Declining returns "declined by user" to the
model, which must not retry silently.

Safety rules enforced in the system prompt and in the tool layer:

- no operation on "all phones" unless explicitly requested (the current selection is the default);
- no invented device state — the assistant must read it first;
- no API keys in answers, no requests for keys in chat;
- no shell command without approval;
- maximum 8 tool rounds per request (configurable 1–20), stop button always available.

## Chat history

Conversations are stored in `data/ai/chats/*.json` (title, provider, model, messages, tool results) and listed in the
chat window. They are local only; nothing is uploaded. Deleting a conversation or all AI data removes the files.

## Troubleshooting

| Symptom | What to do |
|---|---|
| `HTTP 401` / `invalid api key` | Test the key in the provider dialog; check you pasted the whole key and picked the right provider type. |
| `HTTP 404 model not found` | Use *Fetch model list* and pick an existing model id. |
| `HTTP 429` | Rate limit — ControlPhone retries twice, then shows the provider message; wait or switch provider. |
| Timeout | Increase *Timeout* or use a smaller model. |
| Gemini "blocked by content filter" | Rephrase the request; the provider refused the content. |
| Assistant says "no phones targeted" | Select phones in the grid first, or name them ("phone 3"), or ask explicitly for all phones. |
