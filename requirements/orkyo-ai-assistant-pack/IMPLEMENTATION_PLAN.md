# AI assistant — implementation plan and progress

**Branch:** `wirsinddieroboter` in all four repos (foundation, saas, community, documentation).
**Status:** feature complete and committed; verification and polish outstanding (see "Where to pick up").
**Last worked:** 2026-08-21.

## Context

Orkyo's first AI feature. Tenants bring their own Anthropic API key; a robot icon in the
toolbar opens a chat panel that answers questions about workspace data and proposes changes
the user confirms; a per-conflict entry point opens the same panel already looking at that
conflict. This mirrors auto-schedule's established stance: *a proposal mechanism, not an
autopilot*.

### Decisions taken (do not re-litigate)

| Decision | Choice | Why |
|---|---|---|
| Provider | Anthropic only, official `Anthropic` NuGet (12.42.0), `claude-opus-5` | KISS — no provider abstraction to build or test |
| Powers | Read-only tools + user-confirmed writes | A read-only assistant cannot damage data under prompt injection; writes go through existing endpoints |
| Editions | Both. Foundation owns it; SaaS gates by tier; Community always-on | `AllFeaturesEnabledGate` makes Community free of edition-specific code |
| Access | Deny-by-default per user; admins exempt | Admins can replace the key, so a ceiling stored beside it cannot bind them |
| Token budget | Monthly, auto-reset via the `month` column | The month key *is* the reset — no scheduled job to run or fail |
| Transport | SSE, coarse events (no token deltas) | A turn runs tens of seconds; streaming defeats idle proxy timeouts without infra changes |
| Conversation state | Stateless; client echoes an opaque transcript | No tables, no GC, no affinity; safe because prompt/tools are rebuilt server-side each turn |
| Chat loop | Manual, not the SDK tool runner | "A proposal ends the turn" is central control flow, not an interception hook |

## What shipped

### Phase 1 — foundation backend: credentials + allowances (`7d0431d`)
- Migration `1920.foundation.ai_assistant.sql` (tenant DB): `ai_credentials`,
  `ai_user_allowances`, `ai_usage`.
- Key stored as an `IEncryptionService` envelope with the workspace id as GCM associated
  data — a row restored into the wrong workspace fails to decrypt. Write-only: the API
  returns a four-character hint, never the key. Deliberately **not** a `tenant_settings`
  row (that surface returns every value in plaintext to any member).
- `FeatureKeys.AiAssistant` added to `FeatureKeys` **and** `Enforced`.
- `/api/ai/credentials` (GET/PUT/DELETE, `RequireAdminArea`), `/api/ai/allowances`
  (GET/PUT/DELETE, `RequireAdminArea`), `/api/ai/status` (member-visible).
- Audit events on credential and allowance changes; never the key.

### Phase 2 — foundation backend: chat loop (`597edfb`)
- `IAnthropicGateway` seam — the **only** file touching the SDK. Everything above it speaks
  `AiBlock`/`AiToolDefinition`, so the loop is testable against a scripted fake.
- Manual loop: max 6 tool iterations, `MaxTokens` 16000, 5-minute deadline linked to
  `RequestAborted`. Refusal checked before content (classifiers return 200 + empty body).
- Read tools: `get_conflicts`, `get_requests`, `get_request` — compact projections, run
  in-process under the caller's own workspace and role.
- Propose tools (`propose_update_request`, `propose_auto_schedule`) **execute nothing**:
  the loop stops and returns the proposal. Not offered to viewers.
- `AiSystemPrompt` carries the conflict taxonomy with per-kind fixes and the
  treat-content-as-data rule; `AiPromptInvariants` + tests pin those clauses and assert
  every `ConflictKinds` value has guidance.
- SSE endpoint with `X-Accel-Buffering: no`; usage recorded even when a turn fails.

### Phase 3 — foundation frontend (`d78b1a5`)
- `ai-api.ts` incl. a minimal `fetch`+`ReadableStream` SSE reader (EventSource cannot POST).
- Hooks (`useAiAssistant.ts`, `useAiAssistantAvailable.ts`), `qk.ai.*` keys,
  `FeatureKeys.AiAssistant` in `contracts/plans.ts`.
- `AiAssistantSettings.tsx` admin tab: key section (write-only, hint, Test connection,
  Remove) + allowance table listing **every** member, including those with no access.
- `AssistantPanel.tsx` on `ui/sheet.tsx`; `ProposalCard.tsx` renders concrete before/after
  values, not model prose. Apply calls the ordinary `PUT /api/requests/{id}`.
- Toolbar icon (desktop + phone overflow) gated on entitlement **and** the caller's own
  budget. Conflict entry point in `ConflictsTab.tsx`.
- Route/tab wiring; new optional `TenantAppProps.aiAssistantUnavailableRedirectTo`.

### Phase 4 — SaaS gating (`161b71b`)
- `ai_assistant_enabled` in `orkyo-plan-matrix.json`; migration
  `2260.saas.ai_assistant_quota.sql`; `plan-data.ts` regenerated.
- Integration test pins the seeded rows per tier.

### Phase 5 — Community (`875221e`)
- No code needed. README feature list updated.

### Phase 6 — docs and disclosure (`86c7fb6`, saas `06796f6`)
- `docs/user-guide/ai-assistant.md` + `docs/administration/ai-assistant.md`, written to the
  language standard (0.98 violations/100 words — the best in that repo; existing pages run
  2.3–4.5). Cross-linked from the conflicts concept page and resolve-conflicts guide.
- `security.html`: the "We never … use your data for AI training" pledge stays exactly as
  written and is now accompanied by a note drawing the inference-vs-training distinction and
  the four limits that make the feature safe. Feature chip added with tier + BYO-key.

### Uncommitted at pause — architecture guard fixes (verified, 131 tests pass)
The full foundation suite surfaced two ratchet guards that my changes tripped. Both are
fixed in the working tree but **not yet committed**:
- `RequestValidatorCoverageTests` — added `AiRequestValidators.cs`
  (`SaveAiCredentialRequestValidator`, `SaveAiAllowanceRequestValidator`), wired both into
  their endpoints, and allowlisted the three types with no static shape invariant
  (`AiChatEndpointRequest` — bounded by size/length in `AiChatService`; `AiChatRequest` and
  `AiGatewayRequest` — internal, never model-bound).
- `TenantPlanAndEntitlementProviderTests.EnforcedKeys_ExcludeAutoSchedule` — updated to
  expect the fifth enforced key.

## Where to pick up

1. **Commit the guard fixes** (working tree, foundation). Verified: 131 targeted tests pass.
2. **Run the full suites end to end** — `dotnet test` in foundation and saas; `npx vitest run`
   in foundation frontend. The full foundation backend suite last read 3138 passed / 2 failed,
   and those 2 are exactly the guards fixed above.
3. **Manual verification** — nothing has been exercised against a live Anthropic key or a
   running stack. The path in the plan's verification section: free tenant sees upsell and no
   icon → professional admin saves key → Test connection → icon appears for admin only →
   member sees nothing until granted → grant → ask "what conflicts do I have?" → status
   events stream → robot on a conflict → proposal → Apply → conflict count drops. Then limit
   0 and exhausted-budget cases, and a viewer (chat works, no Apply).
4. **SSE through nginx** — verify in staging that `X-Accel-Buffering: no` is honoured.
   Degrades to buffered-but-correct if not, so it is not a blocker.
5. **`AnthropicClient` per-request construction** — confirm handler reuse; if it creates a
   handler per instance, share an `HttpClient` via options to avoid socket exhaustion.
6. **`get_requests` at scale** — it currently calls `GetAllAsync` and filters in memory.
   Fine for a demo workspace, wrong for a large one; move the filter into the repository.

## Known limits (deliberate, documented)

- No token-delta streaming; the event vocabulary supports adding it without a wire change.
- No per-tenant daily quota — per-user monthly budgets cover cost control, and the tenant
  pays for its own tokens.
- No model picker; the `model` column exists as an escape hatch because migrations are
  immutable.
- No server-side conversation persistence.
- Foundation package pin bumps in saas/community are a **release-time** step, deliberately
  not on this branch — local dev consumes the sibling checkout.
