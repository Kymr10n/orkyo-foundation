-- @migration-class: expand

-- An optional Reply-To address for outbox rows. Mail goes out from the SMTP sender, which
-- is a no-reply mailbox in production. The contact-form confirmation sets this column to
-- the contact inbox, so a visitor who answers the confirmation reaches a person. Rows
-- without a value keep NULL and carry no Reply-To header.

ALTER TABLE public.email_outbox
    ADD COLUMN IF NOT EXISTS reply_to character varying(320);
