import { beforeEach, describe, expect, it } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";

import { rememberSessionEndRedirect } from "@foundation/src/lib/utils/session-end";
import { useSharedDemoStore } from "@foundation/src/store/shared-demo-store";
import { STORAGE_KEYS } from "@foundation/src/constants/storage";
import { SharedDemoBanner } from "./SharedDemoBanner";

describe("SharedDemoBanner", () => {
  beforeEach(() => {
    sessionStorage.clear();
    useSharedDemoStore.setState({ noticeHidden: false });
  });

  it("renders nothing for an ordinary session", () => {
    render(<SharedDemoBanner />);
    expect(screen.queryByTestId("shared-demo-banner")).not.toBeInTheDocument();
  });

  it("shows the notice for an ephemeral session", () => {
    rememberSessionEndRedirect("demo");
    render(<SharedDemoBanner />);
    const banner = screen.getByTestId("shared-demo-banner");
    expect(banner).toHaveTextContent(/Shared demo/);
    expect(banner).toHaveTextContent(/Do not enter real or personal information/);
  });

  it("hides on the close button and remembers that for the session", () => {
    rememberSessionEndRedirect("demo");
    render(<SharedDemoBanner />);
    fireEvent.click(screen.getByTestId("shared-demo-banner-hide"));
    expect(screen.queryByTestId("shared-demo-banner")).not.toBeInTheDocument();
    expect(useSharedDemoStore.getState().noticeHidden).toBe(true);
    expect(sessionStorage.getItem(STORAGE_KEYS.SHARED_DEMO_NOTICE)).toContain('"noticeHidden":true');
  });

  it("comes back when the notice is shown again", () => {
    rememberSessionEndRedirect("demo");
    useSharedDemoStore.setState({ noticeHidden: true });
    render(<SharedDemoBanner />);
    expect(screen.queryByTestId("shared-demo-banner")).not.toBeInTheDocument();
    act(() => useSharedDemoStore.getState().setNoticeHidden(false));
    expect(screen.getByTestId("shared-demo-banner")).toBeInTheDocument();
  });
});
