# Orkyo Built-in Insights Semantic Layer — Implementation Pack

This pack defines a lightweight built-in insights capability for Orkyo without introducing a full reporting engine.

Core decision: the product shall expose curated, tenant-safe analytics views and APIs that initially run live queries, but can later be switched to materialized views or snapshot tables without changing the UI or API contracts.

## Contents

1. `01_specification.md` — Product and technical specification
2. `02_data_model_and_sql_contracts.md` — Analytics views, contracts, fields, and source switching model
3. `03_api_contract.md` — Insights API endpoints and response shapes
4. `04_ui_implementation_plan.md` — Built-in dashboard and utilization integration plan
5. `05_implementation_plan.md` — Phased delivery plan for Claude/Copilot
6. `06_validation_and_security.md` — Tenant isolation, correctness, performance, and test strategy
7. `07_claude_execution_prompt.md` — Copy/paste prompt for implementation
