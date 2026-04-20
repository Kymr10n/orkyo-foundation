# Control Plane Migrations

This folder contains migrations for the **Control Plane** database.

## What Goes Here

Migrations related to:
- **Tenant Management**: tenant registry, domains, configurations
- **Authentication**: users, credentials, OAuth providers
- **Authorization**: roles, permissions (global)

## Examples

- `V002__control_plane.sql` - Tenant registry tables
- `V003__users.sql` - User authentication
- `V004__demo_tenant.sql` - Demo tenant seed data

## How to Add a Migration

1. Create: `V###__your_feature.sql` in this folder
2. Done! It auto-discovers and applies

No configuration needed - the script scans this folder automatically.
