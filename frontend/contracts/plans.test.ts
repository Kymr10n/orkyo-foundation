import { describe, it, expect } from 'vitest';
import {
  PlanCodes,
  FeatureKeys,
  planIncludesPremiumFeatures,
  isKnownPlanCode,
  type PlanCode,
} from '@foundation/contracts/plans';

describe('plan codes', () => {
  it('are lowercase machine codes', () => {
    for (const code of Object.values(PlanCodes)) {
      expect(code).toBe(code.toLowerCase());
    }
  });

  it('recognises every code it defines', () => {
    for (const code of Object.values(PlanCodes)) {
      expect(isKnownPlanCode(code)).toBe(true);
    }
  });

  it('rejects display labels', () => {
    // The regression, pinned: the session used to carry "Enterprise" (subscription_tiers
    // .display_name) where the client compares codes, so every plan-derived check read as
    // "not entitled". Keeping this red-line here means a future "fix" that lowercases the
    // label instead of sending the code fails the suite.
    expect(isKnownPlanCode('Enterprise')).toBe(false);
    expect(isKnownPlanCode('Professional')).toBe(false);
    expect(isKnownPlanCode('Community')).toBe(false);
    expect(planIncludesPremiumFeatures('Enterprise' as PlanCode)).toBe(false);
  });
});

describe('planIncludesPremiumFeatures', () => {
  it('excludes Free', () => {
    expect(planIncludesPremiumFeatures(PlanCodes.Free)).toBe(false);
  });

  it('includes Professional and Enterprise', () => {
    expect(planIncludesPremiumFeatures(PlanCodes.Professional)).toBe(true);
    expect(planIncludesPremiumFeatures(PlanCodes.Enterprise)).toBe(true);
  });

  it('includes Community — self-hosted has no commercial gating', () => {
    // Community's server-side gate allows everything (AllFeaturesEnabledGate), so gating it
    // in the UI showed self-hosters padlocks advertising plans they cannot buy.
    expect(planIncludesPremiumFeatures(PlanCodes.Community)).toBe(true);
  });

  it('fails closed on an unknown code', () => {
    expect(planIncludesPremiumFeatures('platinum' as PlanCode)).toBe(false);
  });
});

describe('feature keys', () => {
  it('match the backend FeatureKeys constants', () => {
    // Mirrors backend/core/Security/Features/IFeatureGate.cs — FeatureKeys.Enforced.
    expect(FeatureKeys).toEqual({
      ApiAccess: 'api_access_enabled',
      AuditLog: 'audit_log_enabled',
      DataExport: 'data_export_enabled',
      CalendarFeed: 'calendar_feed_enabled',
    });
  });
});
