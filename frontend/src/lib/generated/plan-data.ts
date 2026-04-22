// ⚠️  AUTO-GENERATED — do not edit manually.
// Source: requirements/orkyo-plan-matrix.yaml
// Generator: scripts/generate-plan-data.mjs
//
// Regenerate with:  node scripts/generate-plan-data.mjs

export interface PlanFeature {
  label: string;
  free: string | boolean;
  professional: string | boolean;
  enterprise: string | boolean;
}

export const PLAN_LIMITS: PlanFeature[] = [
  {
    "label": "Active seats",
    "free": "5",
    "professional": "25",
    "enterprise": "Unlimited"
  },
  {
    "label": "Sites",
    "free": "1",
    "professional": "10",
    "enterprise": "Unlimited"
  },
  {
    "label": "Spaces",
    "free": "15",
    "professional": "250",
    "enterprise": "Unlimited"
  },
  {
    "label": "Storage",
    "free": "1 GB",
    "professional": "10 GB",
    "enterprise": "Unlimited"
  }
];

export const PLAN_FEATURES: PlanFeature[] = [
  {
    "label": "Core application",
    "free": true,
    "professional": true,
    "enterprise": true
  },
  {
    "label": "Multi-site support",
    "free": true,
    "professional": true,
    "enterprise": true
  },
  {
    "label": "Data export",
    "free": true,
    "professional": true,
    "enterprise": true
  },
  {
    "label": "Audit log",
    "free": false,
    "professional": true,
    "enterprise": true
  },
  {
    "label": "API access",
    "free": false,
    "professional": true,
    "enterprise": true
  },
  {
    "label": "Automated backups",
    "free": false,
    "professional": true,
    "enterprise": true
  },
  {
    "label": "Integrations",
    "free": false,
    "professional": false,
    "enterprise": true
  },
  {
    "label": "Advanced analytics",
    "free": false,
    "professional": false,
    "enterprise": true
  },
  {
    "label": "Custom roles",
    "free": false,
    "professional": false,
    "enterprise": true
  },
  {
    "label": "Dedicated deployment",
    "free": false,
    "professional": false,
    "enterprise": true
  },
  {
    "label": "Code escrow",
    "free": false,
    "professional": false,
    "enterprise": true
  },
  {
    "label": "Custom SLA",
    "free": false,
    "professional": false,
    "enterprise": true
  },
  {
    "label": "Custom integrations",
    "free": false,
    "professional": false,
    "enterprise": true
  },
  {
    "label": "Priority support",
    "free": false,
    "professional": true,
    "enterprise": true
  }
];

export interface PlanDisplay {
  key: string;
  displayName: string;
  tagline: string;
  priceLabel: string;
  ctaText: string;
  ctaHref: string;
}

export const PLAN_DISPLAY: PlanDisplay[] = [
  {
    "key": "free",
    "displayName": "Free",
    "tagline": "Get started for free",
    "priceLabel": "CHF 0",
    "ctaText": "Get Started",
    "ctaHref": "/login?auto=1"
  },
  {
    "key": "professional",
    "displayName": "Professional",
    "tagline": "For growing teams",
    "priceLabel": "Contact Us",
    "ctaText": "Contact Sales",
    "ctaHref": "/contact.html"
  },
  {
    "key": "enterprise",
    "displayName": "Enterprise",
    "tagline": "Custom deployment &amp; SLA",
    "priceLabel": "Custom",
    "ctaText": "Contact Sales",
    "ctaHref": "/contact.html"
  }
];
