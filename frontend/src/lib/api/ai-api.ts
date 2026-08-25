/**
 * AI Assistant API
 *
 * Credential and allowance administration (tenant-admin only), the member-facing
 * availability check, and the streaming chat turn.
 *
 * The workspace's API key only ever travels one way. Nothing here reads it back — the
 * admin surface sees a four-character hint and when it was last verified.
 */

import { apiGet, apiPut, apiDelete, apiPost, buildUrl } from '../core/api-client';
import { logger } from '../core/logger';
import { getApiHeaders } from '../core/api-utils';

// ── Credentials ────────────────────────────────────────────────────────────────

export interface AiCredentialStatus {
  configured: boolean;
  provider: string;
  /** Last four characters of the stored key, for recognition only. */
  keyHint: string | null;
  updatedAt: string | null;
  lastVerifiedAt: string | null;
}

export interface AiCredentialTestResult {
  ok: boolean;
  /** `invalid_key` | `network` | `model_unavailable` | `not_configured` */
  reason: string | null;
}

export async function getAiCredential(): Promise<AiCredentialStatus> {
  return apiGet<AiCredentialStatus>('/api/ai/credentials');
}

export async function saveAiCredential(apiKey: string): Promise<AiCredentialStatus> {
  return apiPut<AiCredentialStatus>('/api/ai/credentials', { apiKey });
}

export async function deleteAiCredential(): Promise<void> {
  return apiDelete('/api/ai/credentials');
}

export async function testAiCredential(): Promise<AiCredentialTestResult> {
  return apiPost<AiCredentialTestResult>('/api/ai/credentials/test', {});
}

// ── Allowances ─────────────────────────────────────────────────────────────────

export interface AiUserAllowance {
  userId: string;
  displayName: string | null;
  email: string | null;
  /** Null means unlimited. Zero blocks the user while keeping the grant visible. */
  monthlyTokenLimit: number | null;
  usedInputTokens: number;
  usedOutputTokens: number;
  usedTurns: number;
  /** False means this member has no access at all. */
  granted: boolean;
  usedTotalTokens: number;
}

export async function listAiAllowances(): Promise<AiUserAllowance[]> {
  return apiGet<AiUserAllowance[]>('/api/ai/allowances');
}

export async function saveAiAllowance(
  userId: string,
  monthlyTokenLimit: number | null
): Promise<void> {
  return apiPut(`/api/ai/allowances/${userId}`, { monthlyTokenLimit });
}

export async function revokeAiAllowance(userId: string): Promise<void> {
  return apiDelete(`/api/ai/allowances/${userId}`);
}

// ── Status ─────────────────────────────────────────────────────────────────────

export interface AiStatus {
  available: boolean;
  /** Why not, when unavailable: `not_entitled` | `not_configured` | `not_allowed` | `allowance_exhausted`. */
  reason: string | null;
  monthlyTokenLimit: number | null;
  usedTotalTokens: number;
}

export async function getAiStatus(): Promise<AiStatus> {
  return apiGet<AiStatus>('/api/ai/status');
}

// ── Chat ───────────────────────────────────────────────────────────────────────

/** One block of the conversation. Opaque to the client — held and echoed back verbatim. */
export interface AiBlock {
  type: 'text' | 'thinking' | 'tool_use' | 'tool_result';
  text?: string | null;
  thinking?: string | null;
  signature?: string | null;
  toolUseId?: string | null;
  name?: string | null;
  inputJson?: string | null;
  content?: string | null;
  isError?: boolean | null;
}

export interface AiMessage {
  role: 'user' | 'assistant';
  blocks: AiBlock[];
}

export interface AiProposal {
  toolUseId: string;
  /** `propose_update_request` | `propose_auto_schedule` */
  kind: string;
  /** The proposed change as raw JSON, parsed by the card that renders it. */
  input: string;
}

export interface AiChatRequest {
  message?: string;
  transcript: AiMessage[];
  context?: { type: 'conflict'; requestId: string; kind?: string };
  pendingToolResult?: {
    toolUseId: string;
    status: 'applied' | 'declined' | 'failed';
    detail?: string;
  };
  /** The site on screen. Carries the zone the person's "tomorrow" is measured in. */
  siteId?: string;
}

// ── Saved conversations ────────────────────────────────────────────────────────

/** A conversation in the list: enough to choose one, without carrying its body. */
export interface AiConversationSummary {
  id: string;
  title: string;
  updatedAt: string;
}

/** A conversation in full, as stored. */
export interface AiConversation extends AiConversationSummary {
  entries: AiEntry[];
  transcript: AiMessage[];
}

/**
 * One line of the rendered log. The server stores these without interpreting them, so
 * this shape is owned here.
 */
export interface AiEntry {
  kind: 'user' | 'assistant' | 'action' | 'error';
  text: string;
}

export async function listAiConversations(): Promise<AiConversationSummary[]> {
  return apiGet<AiConversationSummary[]>('/api/ai/conversations');
}

export async function getAiConversation(id: string): Promise<AiConversation> {
  return apiGet<AiConversation>(`/api/ai/conversations/${id}`);
}

/**
 * Creates or replaces a conversation. The client owns the id, so a retry after a failed
 * save writes the same row rather than a duplicate.
 */
export async function saveAiConversation(
  id: string,
  body: { title: string; entries: AiEntry[]; transcript: AiMessage[] },
): Promise<void> {
  await apiPut(`/api/ai/conversations/${id}`, body);
}

export async function deleteAiConversation(id: string): Promise<void> {
  await apiDelete(`/api/ai/conversations/${id}`);
}

/** Everything a turn can emit. The panel renders these as they arrive. */
export type AiChatEvent =
  | { type: 'status'; phase: string; tool: string | null }
  | { type: 'message'; text: string }
  | { type: 'proposal'; proposal: AiProposal }
  | { type: 'ui'; view: string; entityId: string | null; siteId: string | null }
  | { type: 'transcript'; messages: AiMessage[] }
  | { type: 'error'; code: string; message: string }
  | { type: 'done' };

/**
 * Runs one turn, yielding events as the server produces them.
 *
 * `EventSource` cannot POST, so this reads the Server-Sent Events stream off `fetch`
 * directly. The parser is deliberately minimal — the server only ever emits
 * `event:`/`data:` pairs separated by a blank line, plus heartbeat comment lines.
 *
 * Aborting the passed signal stops the turn server-side too, so closing the panel does
 * not leave tokens being spent on an answer nobody will read.
 */
export async function* streamAiChat(
  request: AiChatRequest,
  signal?: AbortSignal
): AsyncGenerator<AiChatEvent> {
  // The URL and headers come from the shared helpers even though the request does not:
  // a relative path would be same-origin, which is wrong when API_BASE_URL points
  // elsewhere, and getApiHeaders carries the tenant and CSRF headers the backend needs
  // to resolve the workspace at all.
  const response = await fetch(buildUrl('/api/ai/chat'), {
    method: 'POST',
    credentials: 'include',
    headers: getApiHeaders('POST'),
    body: JSON.stringify(request),
    signal,
  });

  if (!response.ok || !response.body) {
    yield {
      type: 'error',
      code: 'request_failed',
      message: `The assistant could not be reached (${response.status}).`,
    };
    return;
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });

      // Events are separated by a blank line; anything incomplete stays buffered.
      let separator = buffer.indexOf('\n\n');
      while (separator !== -1) {
        const chunk = buffer.slice(0, separator);
        buffer = buffer.slice(separator + 2);
        const event = parseSseChunk(chunk);
        if (event) yield event;
        separator = buffer.indexOf('\n\n');
      }
    }
  } finally {
    reader.releaseLock();
  }
}

function parseSseChunk(chunk: string): AiChatEvent | null {
  let name = '';
  let data = '';

  for (const line of chunk.split('\n')) {
    if (line.startsWith(':')) continue; // heartbeat
    if (line.startsWith('event:')) name = line.slice(6).trim();
    else if (line.startsWith('data:')) data += line.slice(5).trim();
  }

  if (!name || !data) return null;

  try {
    const payload = JSON.parse(data);
    switch (name) {
      case 'status':
        return { type: 'status', phase: payload.phase, tool: payload.tool ?? null };
      case 'message':
        return { type: 'message', text: payload.text };
      case 'proposal':
        return { type: 'proposal', proposal: payload as AiProposal };
      case 'ui':
        return {
          type: 'ui',
          view: payload.view,
          entityId: payload.entityId ?? null,
          siteId: payload.siteId ?? null,
        };
      case 'transcript':
        return { type: 'transcript', messages: payload as AiMessage[] };
      case 'error':
        return { type: 'error', code: payload.code, message: payload.message };
      case 'done':
        return { type: 'done' };
      default:
        return null;
    }
  } catch {
    // A malformed frame is not worth failing the whole turn over — but a silent drop
    // hides server-side framing bugs, so leave a trace with the frame's shape.
    logger.warn("Dropped malformed SSE frame", { event: name, dataLength: data.length });
    return null;
  }
}
