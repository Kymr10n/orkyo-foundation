-- Audit Events for Tenant Database (Business Events)
-- Applied to tenant databases
-- Records business-relevant events for sites, spaces, requests, etc.

CREATE TABLE IF NOT EXISTS audit_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    actor_user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    actor_type VARCHAR(20) NOT NULL DEFAULT 'user' CHECK (actor_type IN ('user', 'system', 'api')),
    action VARCHAR(100) NOT NULL,
    target_type VARCHAR(50),
    target_id VARCHAR(255),
    metadata JSONB DEFAULT '{}',
    request_id VARCHAR(100),
    ip_address VARCHAR(45),
    user_agent TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_audit_events_actor_user_id ON audit_events(actor_user_id);
CREATE INDEX IF NOT EXISTS idx_audit_events_action ON audit_events(action);
CREATE INDEX IF NOT EXISTS idx_audit_events_target_type ON audit_events(target_type);
CREATE INDEX IF NOT EXISTS idx_audit_events_created_at ON audit_events(created_at);
CREATE INDEX IF NOT EXISTS idx_audit_events_request_id ON audit_events(request_id) WHERE request_id IS NOT NULL;

COMMENT ON TABLE audit_events IS 'Audit trail for business events (sites, spaces, requests, etc.)';
COMMENT ON COLUMN audit_events.action IS 'Action performed, e.g., site.create, space.update, request.submit';
COMMENT ON COLUMN audit_events.target_type IS 'Type of entity affected, e.g., site, space, request';
COMMENT ON COLUMN audit_events.target_id IS 'ID of the entity affected';
COMMENT ON COLUMN audit_events.metadata IS 'Additional context about the action';
