# Orkyo Asset Storage Migration Pack

This pack contains the specification and implementation instructions for moving Orkyo-managed assets, especially floorplans, from Docker-mounted filesystem storage into PostgreSQL-backed asset storage.

## Files

- `01_asset-storage-spec.md` — target architecture and domain model
- `02_backend-implementation-plan.md` — backend implementation steps for Copilot
- `03_migration-plan.md` — migration from filesystem assets to PostgreSQL
- `04_frontend-and-api-impact.md` — API and frontend changes
- `05_acceptance-criteria.md` — validation checklist

## Design intent

The goal is to reduce operational complexity for local, Docker, Portainer, and production deployments by eliminating mandatory asset folder mounts and filesystem permission handling.

The implementation should introduce a generic asset abstraction that initially stores binary data in PostgreSQL, while keeping the door open for future S3/MinIO/object-storage backends.
