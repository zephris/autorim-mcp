# Local AutoRim assistant using Ollama

Status: design outline, not installed or benchmarked. Target: a Windows RTX 3060-class GPU with only 2–4 GiB of dedicated VRAM available to the assistant while RimWorld runs. The available budget matters more than the GPU's advertised total capacity. No automatic cloud inference or CPU inference fallback.

## Candidate model and operating profiles

Primary candidate: `qwen3:1.7b`, Q4_K_M, thinking disabled. Ollama currently lists a roughly 1.4 GB artifact and tool support. This is a candidate for short, constrained commands, not a claim of reliable autonomous colony management. Pin the downloaded digest and verify quantization with `ollama show` before qualifying it. [Model listing](https://ollama.com/library/qwen3:1.7b).

| Available assistant budget | Proposed model | Context cap | Role |
| --- | --- | --- | --- |
| 2 GiB | `qwen3:0.6b` Q4_K_M first | 2048 tokens | Read-only queries and tightly bounded actions after qualification |
| 3–4 GiB | `qwen3:1.7b` Q4_K_M | Start 2048; qualify 4096 | Preferred normal assistant |

The fallback is roughly 523 MB on disk and is also listed with tool support. It will likely need more deterministic workflows and clarification than the primary candidate; compare actual results rather than assuming equal capability. [Fallback listing](https://ollama.com/library/qwen3:0.6b).

Disk size is not total VRAM use. Weights, KV cache, compute buffers and runtime overhead must all fit. These profiles are proposals, not measured fit guarantees. Do not select a larger model merely because its file fits below 4 GB. Keep 384–512 MiB of additional headroom where possible and account for changing game/display usage. If the 2 GiB profile cannot retain headroom, reject the configuration rather than spilling.

## Architecture

User -> small local Node assistant host -> Ollama chat API at 127.0.0.1:11434
                                  -> MCP stdio client -> AutoRim Node server -> game bridge at 127.0.0.1:7789

Ollama supplies inference. A separate host connects the model to MCP: translates selected MCP input schemas into Ollama function schemas, validates returned arguments, executes MCP calls, and supplies compact tool results to the next inference request. Ollama's function calling API returns calls for the application to execute; the model does not directly run MCP or hold the bridge token. [Tool calling](https://docs.ollama.com/capabilities/tool-calling).

Use a lightweight custom Node host rather than a second LLM, vector database or always-on browser interface. Reuse the existing MCP SDK dependency where appropriate. Launch the current AutoRim entry point as a child process using absolute paths; keep stdout exclusively for MCP messages. An existing UI may be added later only if it supports Ollama plus stdio MCP and allows strict tool/context filtering.

Current installation example, for the proposed host's MCP client configuration:

```json
{"command":"C:/nvm4w/nodejs/node.exe","args":["D:/RimPy_Windows/AutoRim/server/dist/index.js"]}
```

Keep bridge authentication in the existing AutoRim server. Store model history, application state and audit records locally; do not send the token to the model.

## Ollama configuration outline

Install Ollama separately when implementation is authorized. Pull only the selected profile; do not run both models together. Example commands for the preferred profile:

```powershell
ollama pull qwen3:1.7b
ollama show qwen3:1.7b
```

Proposed environment for a dedicated Ollama instance, after exiting any existing tray/server process so it actually inherits these settings:

```powershell
$env:OLLAMA_HOST = '127.0.0.1:11434'
$env:OLLAMA_CONTEXT_LENGTH = '2048'
$env:OLLAMA_NUM_PARALLEL = '1'
$env:OLLAMA_MAX_LOADED_MODELS = '1'
$env:OLLAMA_FLASH_ATTENTION = '1'
$env:OLLAMA_KV_CACHE_TYPE = 'q8_0'
ollama serve
```

These settings belong to the proposed Ollama process, not global changes made by this outline. Pin a tested Ollama version and verify supported settings. Single-request/model limits are documented in the [environment configuration source](https://github.com/ollama/ollama/blob/main/envconfig/config.go). Flash Attention and q8 KV caching are documented in the [Ollama FAQ](https://docs.ollama.com/faq).

Per-request baseline:

```json
{
  "model":"qwen3:1.7b",
  "think":false,
  "stream":false,
  "keep_alive":"2m",
  "options":{"num_ctx":2048,"num_predict":192,"temperature":0.1},
  "messages":[{"role":"system","content":"Use only supplied tools and observed IDs. One action at a time. Reply in one short sentence. Never invent success."},{"role":"user","content":"Inspect the Armour stockpile filters."}]
}
```

The host adds task-selected tools to this example. Do not use `num_predict` as a byte limit on tool JSON: if a call is truncated, reject it without execution, then either simplify the task or allow a bounded 384-token retry. Thinking must be disabled in the request, not merely hidden in the UI. [Chat API](https://docs.ollama.com/api/chat), [thinking controls](https://docs.ollama.com/capabilities/thinking).

## Enforcing the no-spill requirement

Interpret no spill as model weights, KV cache and inference compute staying on dedicated GPU memory. Normal system RAM for Windows, Node, tokenization, loading and local logs is still required; zero system-RAM use for the whole application is not possible.

1. Before a session, inspect available dedicated VRAM. Choose the profile against the assistant's explicit 2/3/4 GiB budget, not the whole card. Do not treat a scheduler reserve variable as a hard per-process cap.
2. Warm the chosen model with game actions disabled. Require `ollama ps` to report 100% GPU. Inspect runner logs for full layer offload and GPU KV allocation; CPU model layers or KV fallback fail admission. [Context/offload inspection](https://docs.ollama.com/context-length).
3. For the actual Ollama CUDA runner executable, configure NVIDIA's per-program CUDA Sysmem Fallback Policy to Prefer No Sysmem Fallback where supported. Identify the runner's actual path; applying it only to a parent GUI executable may not affect inference. NVIDIA documents this mechanism for CUDA workloads using a Stable Diffusion example; its application to the selected Ollama runner must be verified. [NVIDIA guidance](https://nvidia.custhelp.com/app/answers/detail/a_id/5490/~/system-memory-fallback-for-stable-diffusion).
4. Check loaded-model telemetry and dedicated/shared GPU memory before and after each inference. `/api/ps` exposes resident-model metadata including size_vram, context and digest, but is not proof against driver paging. Record process-level memory counters where available. [Running models API](https://docs.ollama.com/api/ps).
5. If memory exceeds the assistant budget, inference fails, GPU residency is uncertain, or shared-memory fallback appears: block actions, unload the model and report the failure. A smaller explicitly selected profile may be loaded and qualified again; never silently continue on CPU.

Ollama settings alone do not provide a proven hard VRAM quota or a universal GPU-only guarantee. Preflight can itself trigger allocation; driver policy is needed before warm-up. If the installed driver/runtime cannot prevent or verify fallback, label strict no-spill unsupported and refuse that mode. Do not claim monitoring can prevent every transient allocation or WDDM paging event.

## Small-context operation

Target maximum input around 1600 tokens at a 2048 context setting, leaving approximately 448 for output and template overhead. Count with the model's tokenizer when available; otherwise use conservative bounds calibrated against prompt_eval_count, and refuse oversized requests. Never rely on silent truncation of old messages.

Proposed input allocation: 180 tokens of fixed rules, 180 of current goal/retained state, up to 600 of selected schemas, and up to 640 of user input/current results. These are host design budgets, not model guarantees. Reduce the schema set before dropping relevant task evidence.

- Show only 1–3 action-specific tool schemas per step. Translate compact aliases such as get_storage_filter and set_storage_filter into the existing rimworld_storage actions in host code. Retain required fields, enums and validation rules. Do not expose every subsystem's schemas or a generic execute-any-command tool.
- Begin with deterministic intent routing and a short clarification when ambiguous. Retrieve a narrow state view, act once, verify the affected fields, then finish. Complex requests become bounded substeps rather than a long free-form agent conversation.
- Project tool responses to required IDs, labels, requested fields and blockers. Avoid full colony dumps, full def databases, ASCII maps and all special filters unless needed. Current storage inspection includes a complete def allowlist: cache it in host state and send only relevant entries, counts or membership checks to the model.
- Use deterministic code for comparisons, counts, whitelist intersections, manifest arithmetic and confirmation summaries. A small model should choose among observed alternatives, not reproduce a 100-item JSON whitelist.
- Keep recent goal, resolved IDs, pending action and verified outcome in a compact structured ledger. Store full audit logs outside the inference transcript. Clear completed tool exchanges instead of resending them every turn; refresh live facts before a consequential edit.
- Preserve matching tool-call/result messages within a live tool round. After the round, replace the entire pair with verified ledger state. Do not prune half a pair or turn untrusted tool text into system instructions.
- No routine narration, repeated acknowledgments or hidden reasoning history. Ordinary user replies target 15–40 words. Render predictable success messages in host code directly from validated results, avoiding an extra generation.
- Disable background autonomous planning. Default to on-demand requests, at most four tool calls and one malformed-call retry per user task. Stop with a concise blocker when the budget is exhausted.

These changes reduce prompt growth and latency; short answers alone do not compensate for oversized schemas/results. Persistent context allocation still follows num_ctx, so keep that cap explicit.

## Validation and permissions

Validate every model-selected tool, action, ID, enum, quantity and range against the host's allowlist and current MCP schema. Reject malformed, unknown or truncated calls. Native tool calling is the default; schema-constrained intent JSON is a possible alternative for a narrow workflow, followed by deterministic host translation, not an unchecked arbitrary-command escape hatch. [Structured outputs](https://docs.ollama.com/capabilities/structured-outputs).

Keep the bridge's existing safeguards. The host must not let the model grant itself confirm:true: capture applicable user authorization in host state and bind it to the exact previewed operation. Start qualification read-only, then permit explicitly requested reversible changes. Caravan departure/trading and other consequential actions retain their existing previews and authorization requirements. New caravan features in CARAVAN-FEATURE-PLAN.md remain proposals and must not be advertised as currently callable.

## Implementation and later qualification

1. Implement the small Node host, stdio MCP lifecycle, Ollama chat loop, selective schema adapter and local ledger.
2. Add memory admission/abort logic, action validation, user authorization binding and compact deterministic rendering.
3. Qualify both model profiles against an offline fixture set: correct ID/action selection, invalid arguments, missing information, oversized results, stale state, failures, and attempts to bypass confirmation. Measure success rate, latency, token use and peak dedicated/shared VRAM; no assumed tokens-per-second claims.
4. After separate authorization for runtime QA, measure with RimWorld running through realistic VRAM peaks. Verify full GPU residency, context occupancy and one end-to-end read/edit/read workflow without unauthorized actions. Reject a profile that cannot meet no-spill and task-accuracy requirements.

No models, runtimes or game sessions were installed/launched by this document. No gameplay QA was performed. Vehicle/SOS2 add-ons remain outside this local-assistant outline.
