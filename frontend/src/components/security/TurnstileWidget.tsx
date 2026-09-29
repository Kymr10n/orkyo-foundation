/**
 * Cloudflare Turnstile widget (explicit render).
 *
 * Renders nothing when the deployment provides no site key (local dev,
 * Community) — the backend then accepts token-less submissions (fail-open).
 * Used by the anonymous create-account form; the backend's
 * `RequireChallengeVerification()` filter checks the token it sends.
 * The api.js script is loaded once per page, on first mount with a key.
 */

import { useEffect, useRef } from "react";

import { runtimeConfig } from "@foundation/src/config/runtime";

declare global {
  interface Window {
    turnstile?: {
      render: (el: HTMLElement, opts: Record<string, unknown>) => string;
      reset: (id: string) => void;
      remove: (id: string) => void;
    };
    __orkyoTurnstileOnload?: () => void;
  }
}

let scriptPromise: Promise<void> | null = null;

function loadTurnstileScript(): Promise<void> {
  if (window.turnstile) return Promise.resolve();
  if (!scriptPromise) {
    scriptPromise = new Promise((resolve, reject) => {
      window.__orkyoTurnstileOnload = () => resolve();
      const script = document.createElement("script");
      script.src =
        "https://challenges.cloudflare.com/turnstile/v0/api.js?onload=__orkyoTurnstileOnload&render=explicit";
      script.async = true;
      script.defer = true;
      script.onerror = () => reject(new Error("Failed to load Turnstile script"));
      document.head.appendChild(script);
    });
  }
  return scriptPromise;
}

interface TurnstileWidgetProps {
  /** Called with the challenge token, or null when it expires or errors. */
  onToken: (token: string | null) => void;
  className?: string;
}

export function TurnstileWidget({ onToken, className }: TurnstileWidgetProps) {
  const siteKey = runtimeConfig.turnstileSiteKey;
  const containerRef = useRef<HTMLDivElement>(null);
  const widgetIdRef = useRef<string | null>(null);
  const onTokenRef = useRef(onToken);
  // Synced in an effect, not during render. Turnstile invokes these callbacks from its own
  // script long after mount, so the effect has always flushed by then.
  useEffect(() => {
    onTokenRef.current = onToken;
  });

  useEffect(() => {
    if (!siteKey) return;
    let cancelled = false;

    loadTurnstileScript()
      .then(() => {
        if (cancelled || !containerRef.current || !window.turnstile) return;
        widgetIdRef.current = window.turnstile.render(containerRef.current, {
          sitekey: siteKey,
          "refresh-expired": "auto",
          callback: (token: string) => onTokenRef.current(token),
          "expired-callback": () => onTokenRef.current(null),
          "error-callback": () => onTokenRef.current(null),
        });
      })
      .catch(() => {
        // Script blocked or unreachable — token stays null (backend fails open).
      });

    return () => {
      cancelled = true;
      if (widgetIdRef.current !== null && window.turnstile) {
        window.turnstile.remove(widgetIdRef.current);
        widgetIdRef.current = null;
      }
    };
  }, [siteKey]);

  if (!siteKey) return null;
  return <div ref={containerRef} className={className} />;
}
