import { describe, it, expect } from "vitest";
import {
  DEFAULT_START_TIME,
  DEFAULT_END_TIME,
  DEFAULT_DURATION_VALUE,
  DEFAULT_DURATION_UNIT,
  SPACE_NONE_PLACEHOLDER,
  PLANNING_MODE_CONFIG,
  getPlanningModeLabel,
  getPlanningModeIcon,
  VALIDATION_MESSAGES,
} from "./app";
import { FileText, Layers } from "lucide-react";
import type { PlanningMode } from "@/types/requests";

// ── Time defaults ─────────────────────────────────────────────────────────────

describe("time defaults", () => {
  it("DEFAULT_START_TIME is HH:mm format", () => {
    expect(DEFAULT_START_TIME).toMatch(/^\d{2}:\d{2}$/);
  });

  it("DEFAULT_END_TIME is HH:mm format", () => {
    expect(DEFAULT_END_TIME).toMatch(/^\d{2}:\d{2}$/);
  });

  it("start is before end", () => {
    expect(DEFAULT_START_TIME < DEFAULT_END_TIME).toBe(true);
  });
});

// ── Duration defaults ─────────────────────────────────────────────────────────

describe("duration defaults", () => {
  it("DEFAULT_DURATION_VALUE is a positive number", () => {
    expect(DEFAULT_DURATION_VALUE).toBeGreaterThan(0);
  });

  it("DEFAULT_DURATION_UNIT is 'days'", () => {
    expect(DEFAULT_DURATION_UNIT).toBe("days");
  });
});

// ── SPACE_NONE_PLACEHOLDER ────────────────────────────────────────────────────

describe("SPACE_NONE_PLACEHOLDER", () => {
  it("is a string that cannot be mistaken for a real ID", () => {
    expect(SPACE_NONE_PLACEHOLDER).toBe("__none__");
  });
});

// ── PLANNING_MODE_CONFIG ──────────────────────────────────────────────────────

describe("PLANNING_MODE_CONFIG", () => {
  const modes: PlanningMode[] = ["leaf", "summary", "container"];

  it("has entries for all planning modes", () => {
    for (const mode of modes) {
      expect(PLANNING_MODE_CONFIG[mode]).toBeDefined();
    }
  });

  it("each entry has label, icon, and description", () => {
    for (const mode of modes) {
      const config = PLANNING_MODE_CONFIG[mode];
      expect(typeof config.label).toBe("string");
      expect(config.label.length).toBeGreaterThan(0);
      expect(config.icon).toBeDefined();
      expect(typeof config.description).toBe("string");
      expect(config.description.length).toBeGreaterThan(0);
    }
  });

  it("leaf uses FileText icon", () => {
    expect(PLANNING_MODE_CONFIG.leaf.icon).toBe(FileText);
  });

  it("summary and container use Layers icon", () => {
    expect(PLANNING_MODE_CONFIG.summary.icon).toBe(Layers);
    expect(PLANNING_MODE_CONFIG.container.icon).toBe(Layers);
  });
});

// ── getPlanningModeLabel ──────────────────────────────────────────────────────

describe("getPlanningModeLabel", () => {
  it("returns 'Task' for leaf", () => {
    expect(getPlanningModeLabel("leaf")).toBe("Task");
  });

  it("returns 'Group' for summary", () => {
    expect(getPlanningModeLabel("summary")).toBe("Group");
  });

  it("returns 'Group' for container", () => {
    expect(getPlanningModeLabel("container")).toBe("Group");
  });
});

// ── getPlanningModeIcon ───────────────────────────────────────────────────────

describe("getPlanningModeIcon", () => {
  it("returns FileText for leaf", () => {
    expect(getPlanningModeIcon("leaf")).toBe(FileText);
  });

  it("returns Layers for summary", () => {
    expect(getPlanningModeIcon("summary")).toBe(Layers);
  });
});

// ── VALIDATION_MESSAGES ───────────────────────────────────────────────────────

describe("VALIDATION_MESSAGES", () => {
  it("all values are non-empty strings", () => {
    for (const value of Object.values(VALIDATION_MESSAGES)) {
      expect(typeof value).toBe("string");
      expect(value.length).toBeGreaterThan(0);
    }
  });

  it("has all expected keys", () => {
    const expectedKeys = [
      "REQUEST_NAME_REQUIRED",
      "DURATION_REQUIRED",
      "END_BEFORE_START",
      "DATES_MUST_BE_TOGETHER",
      "CONSTRAINT_ORDER",
      "START_BEFORE_CONSTRAINT",
      "END_AFTER_CONSTRAINT",
      "SAVE_FAILED",
    ];
    for (const key of expectedKeys) {
      expect(VALIDATION_MESSAGES).toHaveProperty(key);
    }
  });
});
