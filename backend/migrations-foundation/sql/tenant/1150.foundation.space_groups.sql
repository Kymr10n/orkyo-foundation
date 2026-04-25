CREATE TABLE public.space_groups (
    id            uuid                     DEFAULT gen_random_uuid() NOT NULL,
    name          character varying(255)   NOT NULL,
    description   text,
    color         character varying(7),
    display_order integer                  DEFAULT 0 NOT NULL,
    created_at    timestamp with time zone DEFAULT now() NOT NULL,
    updated_at    timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT space_groups_pkey PRIMARY KEY (id)
);

CREATE INDEX idx_space_groups_display_order ON public.space_groups USING btree (display_order);

CREATE TRIGGER update_space_groups_updated_at
    BEFORE UPDATE ON public.space_groups
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
