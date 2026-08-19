-- @migration-class: contract

-- Three tables that never got an implementation.
--
--   * invites (1230) planned tenant- and site-scoped invite tokens. The
--     invitation feature was built on the control-plane invitations table
--     (1095) instead; no code ever queried this table in any edition.
--   * request_templates / request_template_requirements (1210) planned a
--     per-user "save as template" feature. No endpoint, repository, or
--     frontend surface was ever built; only the narrative seeder wrote rows,
--     and the criterion usage count read them.
--
-- The entity-scoped template system (templates / template_items) is a
-- different, live feature and stays.
--
-- Forward-fix only: this is a contract migration. If site-scoped invites or
-- personal request templates return as features, they get new schema.

DROP TABLE IF EXISTS public.request_template_requirements;
DROP TABLE IF EXISTS public.request_templates;
DROP TABLE IF EXISTS public.invites;
