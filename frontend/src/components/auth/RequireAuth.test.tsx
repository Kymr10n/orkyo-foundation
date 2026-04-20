import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, act } from "@testing-library/react";
import { AUTH_STAGES, AUTH_EVENTS, AUTH_MESSAGES } from "@/constants/auth";

const mockSend = vi.fn();
const mockUseAuth = vi.fn();

vi.mock("@/contexts/AuthContext", () => ({
  useAuth: () => mockUseAuth(),
  debugAuth: vi.fn(),
}));

import { RequireAuth } from "./RequireAuth";

// ── Helpers ───────────────────────────────────────────────────────────────────

function authState(overrides: Record<string, unknown> = {}) {
  return {
    authStage: AUTH_STAGES.READY,
    error: null,
    send: mockSend,
    ...overrides,
  };
}

function renderGuard(
  requireMembership = true,
  authStage = AUTH_STAGES.READY as string,
) {
  mockUseAuth.mockReturnValue(authState({ authStage }));
  return render(
    <RequireAuth requireMembership={requireMembership}>
      <div data-testid="protected-content">protected</div>
    </RequireAuth>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("RequireAuth", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe("when authenticated (authStage = ready)", () => {
    it("renders children", () => {
      renderGuard(true, AUTH_STAGES.READY);
      expect(screen.getByTestId("protected-content")).toBeInTheDocument();
    });

    it("does not send UNAUTHORIZED", () => {
      renderGuard(true, AUTH_STAGES.READY);
      expect(mockSend).not.toHaveBeenCalledWith({
        type: AUTH_EVENTS.UNAUTHORIZED,
      });
    });
  });

  describe("when loading (authStage = initializing)", () => {
    it("renders blank screen before spinner delay", () => {
      renderGuard(true, AUTH_STAGES.INITIALIZING);
      expect(screen.queryByText(AUTH_MESSAGES.LOADING)).not.toBeInTheDocument();
      expect(screen.queryByTestId("protected-content")).not.toBeInTheDocument();
    });

    it("shows spinner after delay", async () => {
      renderGuard(true, AUTH_STAGES.INITIALIZING);
      await act(async () => {
        vi.advanceTimersByTime(250);
      });
      expect(screen.getByText(AUTH_MESSAGES.LOADING)).toBeInTheDocument();
    });

    it("does not send UNAUTHORIZED while loading", () => {
      renderGuard(true, AUTH_STAGES.INITIALIZING);
      expect(mockSend).not.toHaveBeenCalledWith({
        type: AUTH_EVENTS.UNAUTHORIZED,
      });
    });
  });

  describe("when not authorised (authStage = unauthenticated)", () => {
    it("shows redirect spinner", () => {
      renderGuard(true, AUTH_STAGES.UNAUTHENTICATED);
      expect(
        screen.getByText(AUTH_MESSAGES.REDIRECTING_LOGIN),
      ).toBeInTheDocument();
    });

    it("sends UNAUTHORIZED to the machine", () => {
      renderGuard(true, AUTH_STAGES.UNAUTHENTICATED);
      expect(mockSend).toHaveBeenCalledWith({ type: AUTH_EVENTS.UNAUTHORIZED });
    });

    it("does not render protected content", () => {
      renderGuard(true, AUTH_STAGES.UNAUTHENTICATED);
      expect(screen.queryByTestId("protected-content")).not.toBeInTheDocument();
    });
  });

  describe("requireMembership = false", () => {
    it("renders children when authStage is tos_required", () => {
      renderGuard(false, AUTH_STAGES.TOS_REQUIRED);
      expect(screen.getByTestId("protected-content")).toBeInTheDocument();
    });

    it("renders children when authStage is selecting_tenant", () => {
      renderGuard(false, AUTH_STAGES.SELECTING_TENANT);
      expect(screen.getByTestId("protected-content")).toBeInTheDocument();
    });

    it("renders children when authStage is ready", () => {
      renderGuard(false, AUTH_STAGES.READY);
      expect(screen.getByTestId("protected-content")).toBeInTheDocument();
    });

    it("shows redirect spinner when unauthenticated", () => {
      renderGuard(false, AUTH_STAGES.UNAUTHENTICATED);
      expect(
        screen.getByText(AUTH_MESSAGES.REDIRECTING_LOGIN),
      ).toBeInTheDocument();
    });

    it("sends UNAUTHORIZED when unauthenticated", () => {
      renderGuard(false, AUTH_STAGES.UNAUTHENTICATED);
      expect(mockSend).toHaveBeenCalledWith({ type: AUTH_EVENTS.UNAUTHORIZED });
    });
  });

  describe("loading stages", () => {
    it("treats logging_out as loading (no children, no UNAUTHORIZED)", async () => {
      renderGuard(true, AUTH_STAGES.LOGGING_OUT);
      expect(mockSend).not.toHaveBeenCalledWith({
        type: AUTH_EVENTS.UNAUTHORIZED,
      });
      expect(screen.queryByTestId("protected-content")).not.toBeInTheDocument();

      await act(async () => {
        vi.advanceTimersByTime(250);
      });
      expect(screen.getByText(AUTH_MESSAGES.LOADING)).toBeInTheDocument();
    });

    it("treats redirecting_login as loading", async () => {
      renderGuard(true, AUTH_STAGES.REDIRECTING_LOGIN);
      expect(mockSend).not.toHaveBeenCalledWith({
        type: AUTH_EVENTS.UNAUTHORIZED,
      });
      expect(screen.queryByTestId("protected-content")).not.toBeInTheDocument();

      await act(async () => {
        vi.advanceTimersByTime(250);
      });
      expect(screen.getByText(AUTH_MESSAGES.LOADING)).toBeInTheDocument();
    });
  });

  describe("error states", () => {
    it("shows backend error screen for error_backend", () => {
      mockUseAuth.mockReturnValue(
        authState({
          authStage: AUTH_STAGES.ERROR_BACKEND,
          error: "Server error (500)",
        }),
      );
      render(
        <RequireAuth>
          <div data-testid="protected-content" />
        </RequireAuth>,
      );
      expect(
        screen.getByText(AUTH_MESSAGES.BACKEND_ERROR_TITLE),
      ).toBeInTheDocument();
      expect(screen.getByText("Server error (500)")).toBeInTheDocument();
      expect(screen.queryByTestId("protected-content")).not.toBeInTheDocument();
    });

    it("shows network error screen for error_network", () => {
      mockUseAuth.mockReturnValue(
        authState({
          authStage: AUTH_STAGES.ERROR_NETWORK,
          error: AUTH_MESSAGES.NETWORK_ERROR_DETAIL,
        }),
      );
      render(
        <RequireAuth>
          <div data-testid="protected-content" />
        </RequireAuth>,
      );
      expect(
        screen.getByText(AUTH_MESSAGES.NETWORK_ERROR_TITLE),
      ).toBeInTheDocument();
      expect(screen.queryByTestId("protected-content")).not.toBeInTheDocument();
    });

    it("does not send UNAUTHORIZED for error states", () => {
      renderGuard(true, AUTH_STAGES.ERROR_BACKEND);
      expect(mockSend).not.toHaveBeenCalledWith({
        type: AUTH_EVENTS.UNAUTHORIZED,
      });
    });
  });
});
