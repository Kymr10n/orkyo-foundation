-- V009: Recursive request tree support
-- Adds parent/child hierarchy, planning modes, and sort ordering to requests.

-- ── New columns ─────────────────────────────────────────────────────
ALTER TABLE requests
  ADD COLUMN parent_request_id uuid,
  ADD COLUMN planning_mode character varying(20) NOT NULL DEFAULT 'leaf',
  ADD COLUMN sort_order integer NOT NULL DEFAULT 0;

-- ── Constraints ─────────────────────────────────────────────────────

-- Self-referential foreign key with cascade delete (deleting a parent deletes subtree)
ALTER TABLE requests
  ADD CONSTRAINT fk_requests_parent
    FOREIGN KEY (parent_request_id) REFERENCES requests(id) ON DELETE CASCADE;

-- Prevent self-parenting
ALTER TABLE requests
  ADD CONSTRAINT requests_no_self_parent
    CHECK (parent_request_id IS DISTINCT FROM id);

-- Valid planning modes
ALTER TABLE requests
  ADD CONSTRAINT requests_planning_mode_check
    CHECK (planning_mode IN ('leaf', 'summary', 'container'));

-- ── Indexes ─────────────────────────────────────────────────────────

-- Fast child lookups and ordered retrieval
CREATE INDEX idx_requests_parent_request_id ON requests (parent_request_id);
CREATE INDEX idx_requests_parent_sort ON requests (parent_request_id, sort_order);
