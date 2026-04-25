-- Platform-wide announcements + per-user read tracking.

CREATE TABLE IF NOT EXISTS public.announcements (
    id                  uuid                     PRIMARY KEY DEFAULT gen_random_uuid(),
    title               text                     NOT NULL,
    body                text                     NOT NULL,
    is_important        boolean                  NOT NULL DEFAULT FALSE,
    revision            integer                  NOT NULL DEFAULT 1,
    created_at          timestamp with time zone NOT NULL DEFAULT now(),
    created_by_user_id  uuid                     NOT NULL REFERENCES public.users(id) ON DELETE SET NULL,
    updated_at          timestamp with time zone NOT NULL DEFAULT now(),
    updated_by_user_id  uuid                     NOT NULL REFERENCES public.users(id) ON DELETE SET NULL,
    expires_at          timestamp with time zone NOT NULL
);

COMMENT ON TABLE  public.announcements             IS 'Platform-wide announcements posted by site administrators';
COMMENT ON COLUMN public.announcements.revision    IS 'Incremented on edit; used to invalidate per-user read state without fan-out';
COMMENT ON COLUMN public.announcements.expires_at  IS 'Announcement is active when expires_at > now()';

CREATE INDEX idx_announcements_created_at ON public.announcements (created_at DESC);
CREATE INDEX idx_announcements_expires_at ON public.announcements (expires_at);

CREATE TRIGGER update_announcements_updated_at
    BEFORE UPDATE ON public.announcements
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

-- Per-user read tracking. An announcement is "read" iff read_revision >= announcement.revision.
CREATE TABLE IF NOT EXISTS public.announcement_reads (
    announcement_id uuid                     NOT NULL REFERENCES public.announcements(id) ON DELETE CASCADE,
    user_id         uuid                     NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    read_revision   integer                  NOT NULL,
    read_at         timestamp with time zone NOT NULL DEFAULT now(),
    PRIMARY KEY (announcement_id, user_id)
);

CREATE INDEX idx_announcement_reads_user ON public.announcement_reads (user_id);
