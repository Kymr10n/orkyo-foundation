import { FeatureKeys } from "@foundation/contracts/plans";
import { useFeatureEnabled } from "@foundation/src/hooks/useFeatureEnabled";

/**
 * Whether the current workspace is entitled to the AI assistant.
 *
 * Presentation only — the server enforces `FeatureKeys.AiAssistant` on the credential
 * endpoints, and the chat turn additionally checks the caller's own grant and budget.
 * Entitlement alone does not mean the current user may chat; see `useAiStatus`.
 */
export function useAiAssistantAvailable(): boolean {
  return useFeatureEnabled(FeatureKeys.AiAssistant);
}
