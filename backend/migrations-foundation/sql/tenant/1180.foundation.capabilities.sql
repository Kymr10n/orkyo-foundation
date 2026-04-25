-- Capabilities — typed (criterion, value) pairs attached to a space or a space group.

CREATE TABLE public.space_capabilities (
    id           uuid                     DEFAULT gen_random_uuid() NOT NULL,
    space_id     uuid                     NOT NULL,
    criterion_id uuid                     NOT NULL,
    value        jsonb                    NOT NULL,
    created_at   timestamp with time zone DEFAULT now() NOT NULL,
    updated_at   timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT space_capabilities_pkey PRIMARY KEY (id),
    CONSTRAINT unique_space_criterion UNIQUE (space_id, criterion_id),
    CONSTRAINT space_capabilities_space_id_fkey
        FOREIGN KEY (space_id) REFERENCES public.spaces(id) ON DELETE CASCADE,
    CONSTRAINT space_capabilities_criterion_id_fkey
        FOREIGN KEY (criterion_id) REFERENCES public.criteria(id) ON DELETE CASCADE
);

CREATE INDEX idx_space_capabilities_space_id     ON public.space_capabilities USING btree (space_id);
CREATE INDEX idx_space_capabilities_criterion_id ON public.space_capabilities USING btree (criterion_id);

CREATE TRIGGER update_space_capabilities_updated_at
    BEFORE UPDATE ON public.space_capabilities
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TABLE public.group_capabilities (
    id           uuid                     DEFAULT gen_random_uuid() NOT NULL,
    group_id     uuid                     NOT NULL,
    criterion_id uuid                     NOT NULL,
    value        jsonb                    NOT NULL,
    created_at   timestamp with time zone DEFAULT now() NOT NULL,
    updated_at   timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT group_capabilities_pkey PRIMARY KEY (id),
    CONSTRAINT unique_group_criterion UNIQUE (group_id, criterion_id),
    CONSTRAINT group_capabilities_group_id_fkey
        FOREIGN KEY (group_id) REFERENCES public.space_groups(id) ON DELETE CASCADE,
    CONSTRAINT group_capabilities_criterion_id_fkey
        FOREIGN KEY (criterion_id) REFERENCES public.criteria(id) ON DELETE CASCADE
);

CREATE INDEX idx_group_capabilities_group_id     ON public.group_capabilities USING btree (group_id);
CREATE INDEX idx_group_capabilities_criterion_id ON public.group_capabilities USING btree (criterion_id);

CREATE TRIGGER update_group_capabilities_updated_at
    BEFORE UPDATE ON public.group_capabilities
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
