-- Contact form submissions from marketing pages
-- Stored in the control plane — not tenant-scoped

CREATE TABLE public.contact_submissions (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name            character varying(200) NOT NULL,
    email           character varying(320) NOT NULL,
    company         character varying(200),
    subject         character varying(50)  NOT NULL,
    message         text                   NOT NULL,
    created_at_utc  timestamp with time zone DEFAULT now() NOT NULL
);

CREATE INDEX idx_contact_submissions_email      ON public.contact_submissions USING btree (email);
CREATE INDEX idx_contact_submissions_created_at ON public.contact_submissions USING btree (created_at_utc);

COMMENT ON TABLE public.contact_submissions IS 'Contact form submissions from marketing pages. Reviewed by the team for sales/support follow-up.';
