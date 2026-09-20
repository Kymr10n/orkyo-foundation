import { useMutation, useQuery } from "@tanstack/react-query";
import {
  acceptInvitation,
  validateInvitation,
  type AcceptInvitationRequest,
} from "@foundation/src/lib/api/user-api";
import { qk } from "@foundation/src/lib/api/query-keys";

/**
 * Public invitation landing flow. Both calls run before the invitee has a session, so
 * neither is retried: a bad or expired token must surface its message immediately.
 */
export const useInvitationValidation = (token: string | null) =>
  useQuery({
    queryKey: qk.invitations.validate(token),
    queryFn: () => validateInvitation(token as string),
    enabled: token !== null,
    retry: false,
  });

export const useAcceptInvitation = () =>
  useMutation({
    mutationFn: (data: AcceptInvitationRequest) => acceptInvitation(data),
    retry: false,
  });
