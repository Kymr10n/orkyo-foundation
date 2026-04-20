# Tenant Migrations

This folder contains migrations for **Tenant** databases.

## What Goes Here

Migrations related to business logic:
- **Space Management**: sites, spaces, floorplans
- **Utilization**: requests, requirements, criteria
- **Features**: capabilities, memberships, invites, audit logs

## Examples

- `V001__init.sql` - Core business tables
- `V004__sites.sql` - Site management
- `V014__add_requests.sql` - Request/utilization system
- `V020__remove_fixed_fields.sql` - Schema refactoring

## How to Add a Migration

1. Create: `V###__your_feature.sql` in this folder
2. Done! It auto-discovers and applies

No configuration needed - the script scans this folder automatically.

## Version Numbering

Use the next available version number (V###). Migrations apply in alphanumeric order.
