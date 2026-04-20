-- Platform-wide announcements created by site administrators.
-- Distributed to all users across all tenants.

CREATE TABLE IF NOT EXISTS announcements (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    title               TEXT NOT NULL,
    body                TEXT NOT NULL,
    is_important        BOOLEAN NOT NULL DEFAULT FALSE,
    revision            INT NOT NULL DEFAULT 1,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by_user_id  UUID NOT NULL REFERENCES users(id) ON DELETE SET NULL,
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_by_user_id  UUID NOT NULL REFERENCES users(id) ON DELETE SET NULL,
    expires_at          TIMESTAMPTZ NOT NULL
);

COMMENT ON TABLE  announcements IS 'Platform-wide announcements posted by site administrators';
COMMENT ON COLUMN announcements.revision IS 'Incremented on edit; used to invalidate per-user read state without fan-out';
COMMENT ON COLUMN announcements.expires_at IS 'Announcement is active when expires_at > now()';

CREATE INDEX idx_announcements_created_at ON announcements (created_at DESC);
CREATE INDEX idx_announcements_expires_at ON announcements (expires_at);

CREATE TRIGGER update_announcements_updated_at
    BEFORE UPDATE ON announcements
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- Per-user read tracking.
-- An announcement is "read" for a user iff read_revision >= announcement.revision.
-- Editing an announcement increments revision, making it unread for everyone (O(1) invalidation).

CREATE TABLE IF NOT EXISTS announcement_reads (
    announcement_id UUID NOT NULL REFERENCES announcements(id) ON DELETE CASCADE,
    user_id         UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    read_revision   INT NOT NULL,
    read_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (announcement_id, user_id)
);

CREATE INDEX idx_announcement_reads_user ON announcement_reads (user_id);
