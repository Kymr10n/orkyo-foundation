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

---

# Review 2026-08-25 — external verification spec

An external architecture spec (ChatGPT-authored, "Implementation Verification Specification")
was reviewed against the implementation. Verdict: **the architecture meets or exceeds it
where it matters most.** The spec's §12-13 imagine the AI executing writes after
conversational confirmation; this implementation is stricter — the AI never writes at all.
A proposal ends the turn and the change happens through the person's own session and the
ordinary endpoint. That satisfies "the AI interprets, Orkyo decides" more completely than
the spec's own target architecture.

PASS: authorization and tenant isolation (tools run in-process under the caller's OrgContext
and role), untrusted-model-output handling (closed catalogs, Guid checks, clamped limits),
model independence (`IAnthropicGateway` is exactly the adapter the spec draws), business
rules outside the prompt, structured results, context-by-tool rather than data dumps.

**Note the spec itself is stale**: it is written in pre-0.18 vocabulary (Space, Allocation,
built-in types) and some of its capability inventory describes a product shape that no
longer exists. Read it for principles, not for its nouns.

## Fixed in this pass

- **Stale-preview apply returned 500, not 409.** `AutoScheduleService` threw
  `InvalidOperationException`, which matches no arm in `AppExceptionHandler`; the apply
  dialog branches on 409, so its "close and re-run" message never appeared. Now
  `ConflictException`. (Found by the spec's idempotency question.)
- **The prompt taught retired vocabulary** — "spaces, people, tools" — a release after
  0.18.0 renamed them. Rewritten, plus a new `# The model` section explaining
  tenant-defined resource types, Stations vs Assets, and request-vs-assignment, and a
  clarification policy. `AiPromptInvariants.RetiredPhrases` + a test now fail loudly if the
  old words return.
- **Entity resolution was missing** — the model could only search requests. New `search`
  tool wrapping the existing `ISearchRepository` (the same index the command palette uses),
  covering resource/request/group/site/template/criterion.
- **Timezone**: per-site IANA zones existed and drove `SchedulingEngine`, but the assistant
  lived in UTC. The chat request now carries the selected site and `Dynamic()` names its
  zone.

## Open decisions (not gaps to patch in the AI layer)

- **Request auditing.** `PUT /api/requests/{id}` writes no audit event — for the assistant
  *or* the UI; `TenantAuditActions` has no `request.*` action at all, and `audit_events` has
  no origin column (only free-form `metadata`). Distinguishing "changed via assistant" is
  worth having, but it starts as a product decision about auditing request changes at all.
  Adding it only for the assistant would put a domain concern in the AI layer.
- **Stateless conversation.** The spec §16 wants structured multi-turn state. The transcript
  echo is a recorded decision (no tables, no GC, no affinity) and stands.
- **AI-executed writes** (§7.4/7.5) would be a stance change, not a gap.
- **`resource` in the open_view catalog** — its route needs the client's resource-type list.

---

# Conversations and panel width — 2026-08-25

## Server-side conversations supersede the "no tables" decision

The original decision (§Decisions taken: *Conversation state — stateless; client echoes an
opaque transcript*) is superseded **for storage only**. What made it right still holds and
is preserved:

- **The turn is still stateless.** The chat endpoint neither reads nor writes a
  conversation, and the client still echoes the transcript on every turn. Storage is a
  notebook beside the conversation, never a dependency of it — if every conversation call
  failed, the assistant would still answer.
- **No GC job.** The objection to tables was the cleanup that comes with them. The cap
  enforces itself on write: `AiConversationService.KeepPerUser` (20) trims the owner's
  older rows in the same call that adds one, so there is no schedule to run or to fail.
- **No affinity.** Rows are read by id, so nothing pins a person to an instance.

New surface: `ai_conversations` (migration `1930`, expand), repository, service, and
`/api/ai/conversations` CRUD. Both blobs are stored opaquely — the server never
interprets `entries` or `transcript` — so the panel can change what it records without a
migration.

**Ownership is the security property.** The service takes the owner from
`ICurrentPrincipal`, never from the request, so no payload can select someone else's rows;
the repository filters on `user_id` in every statement, including the upsert's WHERE, so a
guessed id cannot hijack a row. Tested from both sides.

## MaxTranscriptBytes is now enforced

It was declared and referenced nowhere. The chat turn checks both ceilings, and the save
path refuses what could never be sent — otherwise persistence would have made an unusable
conversation permanent, restoring on every reload and failing on every send.

`conversation_too_long` also became actionable: the panel branches on the code and offers
"Start a new conversation" instead of printing advice it gave no way to follow.

## Panel width

`orkyo.assistant.width` in localStorage via `usePanelWidth`. Width belongs to the screen
someone is sitting at, not to their account, so it does not travel with them the way
conversations do. Clamped to the viewport on read, so a width saved on a large monitor
cannot cover a laptop. Phone keeps the full-bleed panel and shows no handle; the panel's
breakpoint moved from `sm:` to `md:` to match `useBreakpoint`, which they had disagreed on
by 128px.
