-- @migration-class: expand

-- Saved assistant conversations, so a thread survives a reload and follows the person
-- between devices.
--
-- The turn itself stays stateless: the server never reads this table while answering.
-- The client still echoes the transcript on every turn, exactly as before. This is a
-- notebook the client writes through its own CRUD endpoints — nothing in the chat loop
-- depends on a row existing, and losing one costs history, never an answer.
--
-- entries is what the panel renders; transcript is what the model reads. Both are
-- opaque here: the server stores and returns them without interpreting either, so the
-- shape can change on the client without a migration.
--
-- Rows belong to one person. Every query filters on user_id — a workspace member must
-- never read another member's conversations, and a transcript quotes workspace data
-- the reader may not otherwise be entitled to.
--
-- There is no cleanup job. The service keeps only the newest conversations per person
-- and deletes the rest on write, so the cap enforces itself on the path that creates
-- the pressure.

BEGIN;

CREATE TABLE IF NOT EXISTS public.ai_conversations (
    id         uuid        PRIMARY KEY,
    user_id    uuid        NOT NULL,
    title      text        NOT NULL,
    entries    jsonb       NOT NULL,
    transcript jsonb       NOT NULL,
    created_at timestamptz NOT NULL DEFAULT NOW(),
    updated_at timestamptz NOT NULL DEFAULT NOW()
);

-- The list query: one person's conversations, newest first.
CREATE INDEX IF NOT EXISTS idx_ai_conversations_user_updated
    ON public.ai_conversations (user_id, updated_at DESC);

COMMIT;

-- Rollback: DROP TABLE public.ai_conversations;
