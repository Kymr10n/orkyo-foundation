import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { NowLine } from "./NowLine";

describe("NowLine", () => {
  it("renders the marker and 'Now' label when now is within the view range", () => {
    const { getByTestId, getByText } = render(
      <NowLine nowMs={50} viewStartMs={0} viewEndMs={100} />,
    );
    expect(getByTestId("now-line")).toBeInTheDocument();
    expect(getByText("Now")).toBeInTheDocument();
  });

  it("renders nothing when now is outside the view range", () => {
    const { queryByTestId, queryByText } = render(
      <NowLine nowMs={150} viewStartMs={0} viewEndMs={100} />,
    );
    expect(queryByTestId("now-line")).not.toBeInTheDocument();
    expect(queryByText("Now")).not.toBeInTheDocument();
  });
});
