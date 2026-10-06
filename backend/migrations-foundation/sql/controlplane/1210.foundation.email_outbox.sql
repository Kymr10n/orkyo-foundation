-- @migration-class: expand
-- Description: Transactional outbox for outbound mail.
--
-- Every mail the application sends is written here, fully rendered, before any
-- delivery attempt. The attempt that follows is best effort; a row that is still
-- pending is retried by the worker's email-outbox job with a growing delay and
-- becomes 'dead' after the attempt limit. Before this table, a send ran in a
-- fire-and-forget task and a crash, an OOM kill or a slot drain lost the mail
-- with no record. Bodies carry live tokens (invitation and confirmation links),
-- so they are cleared on delivery and sent rows are pruned after a day; dead
-- rows keep their body for inspection and are pruned after thirty days.

CREATE TABLE IF NOT EXISTS public.email_outbox (
    id              uuid                     DEFAULT gen_random_uuid() NOT NULL,
    to_email        character varying(320)   NOT NULL,
    to_name         character varying(255)   NOT NULL,
    subject         text                     NOT NULL,
    html_body       text,
    text_body       text,
    status          character varying(16)    DEFAULT 'pending' NOT NULL,
    attempts        integer                  DEFAULT 0 NOT NULL,
    next_attempt_at timestamp with time zone DEFAULT now() NOT NULL,
    last_error      text,
    created_at      timestamp with time zone DEFAULT now() NOT NULL,
    sent_at         timestamp with time zone,

    CONSTRAINT email_outbox_pkey PRIMARY KEY (id),
    CONSTRAINT email_outbox_status_check
        CHECK (status IN ('pending', 'sent', 'dead'))
);

-- The drain query: pending rows that are due, oldest first.
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_email_outbox_due
    ON public.email_outbox (next_attempt_at)
    WHERE status = 'pending';

-- The pruner: sent and dead rows by age.
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_email_outbox_settled
    ON public.email_outbox (status, created_at)
    WHERE status <> 'pending';
