CREATE OR REPLACE FUNCTION public.update_requests_updated_at() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$;
CREATE OR REPLACE FUNCTION public.update_updated_at_column() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
	NEW.updated_at = NOW();
	RETURN NEW;
END;
$$;
CREATE TABLE public.audit_events (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    actor_user_id uuid,
    actor_type character varying(20) DEFAULT 'user'::character varying NOT NULL,
    action character varying(100) NOT NULL,
    target_type character varying(50),
    target_id character varying(255),
    metadata jsonb DEFAULT '{}'::jsonb,
    request_id character varying(100),
    ip_address character varying(45),
    user_agent text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT audit_events_actor_type_check CHECK (((actor_type)::text = ANY ((ARRAY['user'::character varying, 'system'::character varying, 'api'::character varying])::text[])))
);
CREATE TABLE public.criteria (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(100) NOT NULL,
    description text,
    data_type character varying(20) NOT NULL,
    enum_values jsonb,
    unit character varying(20),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT criteria_data_type_check CHECK (((data_type)::text = ANY ((ARRAY['Boolean'::character varying, 'Number'::character varying, 'String'::character varying, 'Enum'::character varying])::text[])))
);
CREATE TABLE public.feedback (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid,
    feedback_type character varying(20) NOT NULL,
    title character varying(200) NOT NULL,
    description text,
    page_url text,
    user_agent text,
    status character varying(20) DEFAULT 'new'::character varying NOT NULL,
    admin_notes text,
    github_issue_url text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT feedback_feedback_type_check CHECK (((feedback_type)::text = ANY ((ARRAY['bug'::character varying, 'feature'::character varying, 'question'::character varying, 'other'::character varying])::text[]))),
    CONSTRAINT feedback_status_check CHECK (((status)::text = ANY ((ARRAY['new'::character varying, 'reviewed'::character varying, 'resolved'::character varying, 'wont_fix'::character varying])::text[])))
);
CREATE TABLE public.group_capabilities (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    group_id uuid NOT NULL,
    criterion_id uuid NOT NULL,
    value jsonb NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);
CREATE TABLE public.invites (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    email character varying(320) NOT NULL,
    invited_by_user_id uuid,
    scope character varying(20) NOT NULL,
    site_id uuid,
    role character varying(50) NOT NULL,
    token_hash character varying(255) NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    accepted_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT invites_scope_check CHECK (((scope)::text = ANY ((ARRAY['tenant'::character varying, 'site'::character varying])::text[])))
);
CREATE TABLE public.memberships (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    site_id uuid NOT NULL,
    role character varying(50) DEFAULT 'read_only'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT memberships_role_check CHECK (((role)::text = ANY ((ARRAY['read_only'::character varying, 'writer'::character varying, 'site_admin'::character varying])::text[])))
);
CREATE TABLE public.preset_applications (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    preset_id character varying(100) NOT NULL,
    preset_version character varying(20) NOT NULL,
    applied_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone,
    applied_by_user_id uuid
);
CREATE TABLE public.preset_mappings (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    preset_application_id uuid NOT NULL,
    entity_type character varying(50) NOT NULL,
    logical_key character varying(100) NOT NULL,
    entity_id uuid NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);
CREATE TABLE public.request_requirements (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    request_id uuid NOT NULL,
    criterion_id uuid NOT NULL,
    value jsonb NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE public.request_template_requirements (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    template_id uuid NOT NULL,
    criterion_id uuid NOT NULL,
    value jsonb NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);
CREATE TABLE public.request_templates (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    description text,
    minimal_duration_value integer NOT NULL,
    minimal_duration_unit character varying(20) NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT request_templates_minimal_duration_unit_check CHECK (((minimal_duration_unit)::text = ANY ((ARRAY['minutes'::character varying, 'hours'::character varying, 'days'::character varying, 'weeks'::character varying, 'months'::character varying, 'years'::character varying])::text[]))),
    CONSTRAINT request_templates_minimal_duration_value_check CHECK ((minimal_duration_value > 0))
);
CREATE TABLE public.requests (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(200) NOT NULL,
    description text,
    space_id uuid,
    request_item_id character varying(100),
    start_ts timestamp with time zone,
    end_ts timestamp with time zone,
    minimal_duration_value integer NOT NULL,
    minimal_duration_unit character varying(20) NOT NULL,
    status character varying(20) DEFAULT 'planned'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    earliest_start_ts timestamp with time zone,
    latest_end_ts timestamp with time zone,
    actual_duration_value integer,
    actual_duration_unit character varying(20),
    CONSTRAINT requests_actual_duration_complete_check CHECK ((((actual_duration_value IS NULL) AND (actual_duration_unit IS NULL)) OR ((actual_duration_value IS NOT NULL) AND (actual_duration_unit IS NOT NULL)))),
    CONSTRAINT requests_actual_duration_unit_check CHECK (((actual_duration_unit IS NULL) OR ((actual_duration_unit)::text = ANY ((ARRAY['minutes'::character varying, 'hours'::character varying, 'days'::character varying, 'weeks'::character varying, 'months'::character varying, 'years'::character varying])::text[])))),
    CONSTRAINT requests_actual_duration_value_check CHECK (((actual_duration_value IS NULL) OR (actual_duration_value > 0))),
    CONSTRAINT requests_constraint_dates_order_check CHECK (((earliest_start_ts IS NULL) OR (latest_end_ts IS NULL) OR (earliest_start_ts < latest_end_ts))),
    CONSTRAINT requests_minimal_duration_unit_check CHECK (((minimal_duration_unit)::text = ANY ((ARRAY['minutes'::character varying, 'hours'::character varying, 'days'::character varying, 'weeks'::character varying, 'months'::character varying, 'years'::character varying])::text[]))),
    CONSTRAINT requests_minimal_duration_value_check CHECK ((minimal_duration_value > 0)),
    CONSTRAINT requests_scheduled_end_within_constraints_check CHECK (((end_ts IS NULL) OR (latest_end_ts IS NULL) OR (end_ts <= latest_end_ts))),
    CONSTRAINT requests_scheduled_within_constraints_check CHECK (((start_ts IS NULL) OR (earliest_start_ts IS NULL) OR (start_ts >= earliest_start_ts))),
    CONSTRAINT requests_status_check CHECK (((status)::text = ANY ((ARRAY['planned'::character varying, 'in_progress'::character varying, 'done'::character varying, 'cancelled'::character varying])::text[]))),
    CONSTRAINT valid_time_range CHECK ((((start_ts IS NULL) AND (end_ts IS NULL)) OR ((start_ts IS NOT NULL) AND (end_ts IS NOT NULL) AND (end_ts > start_ts))))
);
CREATE TABLE public.sites (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(255) NOT NULL,
    code character varying(63) NOT NULL,
    description text,
    address text,
    attributes jsonb,
    floorplan_image_path character varying(500),
    floorplan_mime_type character varying(100),
    floorplan_file_size_bytes bigint,
    floorplan_width_px integer,
    floorplan_height_px integer,
    floorplan_uploaded_at timestamp with time zone,
    floorplan_uploaded_by_user_id uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);
CREATE TABLE public.space_capabilities (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    space_id uuid NOT NULL,
    criterion_id uuid NOT NULL,
    value jsonb NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);
CREATE TABLE public.space_groups (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(255) NOT NULL,
    description text,
    color character varying(7),
    display_order integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);
CREATE TABLE public.spaces (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    site_id uuid NOT NULL,
    code character varying(63),
    name character varying(255) NOT NULL,
    description text,
    is_physical boolean DEFAULT false NOT NULL,
    geometry jsonb,
    properties jsonb DEFAULT '{}'::jsonb,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    group_id uuid,
    CONSTRAINT check_physical_has_geometry CHECK ((((is_physical = true) AND (geometry IS NOT NULL)) OR (is_physical = false)))
);
CREATE TABLE public.template_items (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    template_id uuid NOT NULL,
    criterion_id uuid NOT NULL,
    value jsonb NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);
CREATE TABLE public.templates (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(255) NOT NULL,
    description text,
    entity_type character varying(20) NOT NULL,
    duration_value integer,
    duration_unit character varying(20),
    fixed_start boolean DEFAULT false,
    fixed_end boolean DEFAULT false,
    fixed_duration boolean DEFAULT true,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT templates_duration_unit_check CHECK (((duration_unit IS NULL) OR ((duration_unit)::text = ANY ((ARRAY['minutes'::character varying, 'hours'::character varying, 'days'::character varying, 'weeks'::character varying])::text[])))),
    CONSTRAINT templates_duration_value_check CHECK (((duration_value IS NULL) OR (duration_value > 0))),
    CONSTRAINT templates_entity_type_check CHECK (((entity_type)::text = ANY ((ARRAY['request'::character varying, 'space'::character varying, 'group'::character varying])::text[])))
);
CREATE TABLE public.user_preferences (
    user_id uuid NOT NULL,
    preferences jsonb DEFAULT '{}'::jsonb NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);
CREATE TABLE public.users (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    email character varying(320) NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    display_name character varying(255),
    synced_at timestamp with time zone DEFAULT now()
);
ALTER TABLE ONLY public.audit_events
    ADD CONSTRAINT audit_events_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.criteria
    ADD CONSTRAINT criteria_name_key UNIQUE (name);
ALTER TABLE ONLY public.criteria
    ADD CONSTRAINT criteria_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.feedback
    ADD CONSTRAINT feedback_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.group_capabilities
    ADD CONSTRAINT group_capabilities_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.invites
    ADD CONSTRAINT invites_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.memberships
    ADD CONSTRAINT memberships_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.memberships
    ADD CONSTRAINT memberships_user_id_site_id_key UNIQUE (user_id, site_id);
ALTER TABLE ONLY public.preset_applications
    ADD CONSTRAINT preset_applications_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.preset_mappings
    ADD CONSTRAINT preset_mappings_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.request_requirements
    ADD CONSTRAINT request_requirements_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.request_requirements
    ADD CONSTRAINT request_requirements_request_id_criterion_id_key UNIQUE (request_id, criterion_id);
ALTER TABLE ONLY public.request_template_requirements
    ADD CONSTRAINT request_template_requirements_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.request_template_requirements
    ADD CONSTRAINT request_template_requirements_template_id_criterion_id_key UNIQUE (template_id, criterion_id);
ALTER TABLE ONLY public.request_templates
    ADD CONSTRAINT request_templates_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.requests
    ADD CONSTRAINT requests_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.sites
    ADD CONSTRAINT sites_code_key UNIQUE (code);
ALTER TABLE ONLY public.sites
    ADD CONSTRAINT sites_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.space_capabilities
    ADD CONSTRAINT space_capabilities_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.space_groups
    ADD CONSTRAINT space_groups_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.spaces
    ADD CONSTRAINT spaces_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.template_items
    ADD CONSTRAINT template_items_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.templates
    ADD CONSTRAINT templates_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.group_capabilities
    ADD CONSTRAINT unique_group_criterion UNIQUE (group_id, criterion_id);
ALTER TABLE ONLY public.space_capabilities
    ADD CONSTRAINT unique_space_criterion UNIQUE (space_id, criterion_id);
ALTER TABLE ONLY public.template_items
    ADD CONSTRAINT unique_template_criterion UNIQUE (template_id, criterion_id);
ALTER TABLE ONLY public.preset_applications
    ADD CONSTRAINT uq_preset_applications_preset_id UNIQUE (preset_id);
ALTER TABLE ONLY public.preset_mappings
    ADD CONSTRAINT uq_preset_mappings_key UNIQUE (preset_application_id, entity_type, logical_key);
ALTER TABLE ONLY public.user_preferences
    ADD CONSTRAINT user_preferences_pkey PRIMARY KEY (user_id);
ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_email_key UNIQUE (email);
ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);
CREATE INDEX idx_audit_events_action ON public.audit_events USING btree (action);
CREATE INDEX idx_audit_events_actor_user_id ON public.audit_events USING btree (actor_user_id);
CREATE INDEX idx_audit_events_created_at ON public.audit_events USING btree (created_at);
CREATE INDEX idx_audit_events_request_id ON public.audit_events USING btree (request_id) WHERE (request_id IS NOT NULL);
CREATE INDEX idx_audit_events_target_type ON public.audit_events USING btree (target_type);
CREATE INDEX idx_criteria_name ON public.criteria USING btree (name);
CREATE INDEX idx_feedback_created ON public.feedback USING btree (created_at DESC);
CREATE INDEX idx_feedback_status ON public.feedback USING btree (status);
CREATE INDEX idx_group_capabilities_criterion_id ON public.group_capabilities USING btree (criterion_id);
CREATE INDEX idx_group_capabilities_group_id ON public.group_capabilities USING btree (group_id);
CREATE INDEX idx_invites_email ON public.invites USING btree (email);
CREATE INDEX idx_invites_expires_at ON public.invites USING btree (expires_at);
CREATE INDEX idx_invites_token_hash ON public.invites USING btree (token_hash);
CREATE INDEX idx_memberships_site_id ON public.memberships USING btree (site_id);
CREATE INDEX idx_memberships_user_id ON public.memberships USING btree (user_id);
CREATE INDEX idx_preset_applications_preset_id ON public.preset_applications USING btree (preset_id);
CREATE INDEX idx_preset_mappings_application ON public.preset_mappings USING btree (preset_application_id);
CREATE INDEX idx_preset_mappings_entity ON public.preset_mappings USING btree (entity_type, entity_id);
CREATE INDEX idx_request_requirements_criterion_id ON public.request_requirements USING btree (criterion_id);
CREATE INDEX idx_request_requirements_criterion_value ON public.request_requirements USING btree (criterion_id, value);
CREATE INDEX idx_request_requirements_request_id ON public.request_requirements USING btree (request_id);
CREATE INDEX idx_request_templates_user_id ON public.request_templates USING btree (user_id);
CREATE UNIQUE INDEX idx_request_templates_user_name ON public.request_templates USING btree (user_id, name);
CREATE INDEX idx_requests_end_ts ON public.requests USING btree (end_ts);
CREATE INDEX idx_requests_space_id ON public.requests USING btree (space_id);
CREATE INDEX idx_requests_start_ts ON public.requests USING btree (start_ts);
CREATE INDEX idx_requests_status ON public.requests USING btree (status);
CREATE INDEX idx_requests_time_range ON public.requests USING btree (start_ts, end_ts);
CREATE INDEX idx_requests_time_status ON public.requests USING btree (start_ts, end_ts, status) WHERE ((start_ts IS NOT NULL) AND (end_ts IS NOT NULL));
CREATE INDEX idx_sites_code ON public.sites USING btree (code);
CREATE INDEX idx_sites_floorplan_path ON public.sites USING btree (floorplan_image_path) WHERE (floorplan_image_path IS NOT NULL);
CREATE INDEX idx_space_capabilities_criterion_id ON public.space_capabilities USING btree (criterion_id);
CREATE INDEX idx_space_capabilities_space_id ON public.space_capabilities USING btree (space_id);
CREATE INDEX idx_space_groups_display_order ON public.space_groups USING btree (display_order);
CREATE INDEX idx_spaces_code ON public.spaces USING btree (code) WHERE (code IS NOT NULL);
CREATE INDEX idx_spaces_group_id ON public.spaces USING btree (group_id);
CREATE INDEX idx_spaces_site_code ON public.spaces USING btree (site_id, code);
CREATE INDEX idx_spaces_site_id ON public.spaces USING btree (site_id);
CREATE INDEX idx_template_items_criterion_id ON public.template_items USING btree (criterion_id);
CREATE INDEX idx_template_items_template_id ON public.template_items USING btree (template_id);
CREATE INDEX idx_template_requirements_criterion_id ON public.request_template_requirements USING btree (criterion_id);
CREATE INDEX idx_template_requirements_template_id ON public.request_template_requirements USING btree (template_id);
CREATE INDEX idx_templates_entity_type ON public.templates USING btree (entity_type);
CREATE INDEX idx_templates_name ON public.templates USING btree (name);
CREATE INDEX idx_user_preferences_user_id ON public.user_preferences USING btree (user_id);
CREATE TRIGGER trigger_requests_updated_at BEFORE UPDATE ON public.requests FOR EACH ROW EXECUTE FUNCTION public.update_requests_updated_at();
CREATE TRIGGER update_group_capabilities_updated_at BEFORE UPDATE ON public.group_capabilities FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
CREATE TRIGGER update_memberships_updated_at BEFORE UPDATE ON public.memberships FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
CREATE TRIGGER update_request_templates_updated_at BEFORE UPDATE ON public.request_templates FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
CREATE TRIGGER update_sites_updated_at BEFORE UPDATE ON public.sites FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
CREATE TRIGGER update_space_capabilities_updated_at BEFORE UPDATE ON public.space_capabilities FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
CREATE TRIGGER update_space_groups_updated_at BEFORE UPDATE ON public.space_groups FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
CREATE TRIGGER update_spaces_updated_at BEFORE UPDATE ON public.spaces FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
CREATE TRIGGER update_template_items_updated_at BEFORE UPDATE ON public.template_items FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
CREATE TRIGGER update_templates_updated_at BEFORE UPDATE ON public.templates FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
ALTER TABLE ONLY public.audit_events
    ADD CONSTRAINT audit_events_actor_user_id_fkey FOREIGN KEY (actor_user_id) REFERENCES public.users(id) ON DELETE SET NULL;
ALTER TABLE ONLY public.feedback
    ADD CONSTRAINT feedback_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE SET NULL;
ALTER TABLE ONLY public.group_capabilities
    ADD CONSTRAINT group_capabilities_criterion_id_fkey FOREIGN KEY (criterion_id) REFERENCES public.criteria(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.group_capabilities
    ADD CONSTRAINT group_capabilities_group_id_fkey FOREIGN KEY (group_id) REFERENCES public.space_groups(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.invites
    ADD CONSTRAINT invites_invited_by_user_id_fkey FOREIGN KEY (invited_by_user_id) REFERENCES public.users(id) ON DELETE SET NULL;
ALTER TABLE ONLY public.invites
    ADD CONSTRAINT invites_site_id_fkey FOREIGN KEY (site_id) REFERENCES public.sites(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.memberships
    ADD CONSTRAINT memberships_site_id_fkey FOREIGN KEY (site_id) REFERENCES public.sites(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.memberships
    ADD CONSTRAINT memberships_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.preset_applications
    ADD CONSTRAINT preset_applications_applied_by_user_id_fkey FOREIGN KEY (applied_by_user_id) REFERENCES public.users(id) ON DELETE SET NULL;
ALTER TABLE ONLY public.preset_mappings
    ADD CONSTRAINT preset_mappings_preset_application_id_fkey FOREIGN KEY (preset_application_id) REFERENCES public.preset_applications(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.request_requirements
    ADD CONSTRAINT request_requirements_criterion_id_fkey FOREIGN KEY (criterion_id) REFERENCES public.criteria(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.request_requirements
    ADD CONSTRAINT request_requirements_request_id_fkey FOREIGN KEY (request_id) REFERENCES public.requests(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.request_template_requirements
    ADD CONSTRAINT request_template_requirements_criterion_id_fkey FOREIGN KEY (criterion_id) REFERENCES public.criteria(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.request_template_requirements
    ADD CONSTRAINT request_template_requirements_template_id_fkey FOREIGN KEY (template_id) REFERENCES public.request_templates(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.request_templates
    ADD CONSTRAINT request_templates_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.requests
    ADD CONSTRAINT requests_space_id_fkey FOREIGN KEY (space_id) REFERENCES public.spaces(id) ON DELETE SET NULL;
ALTER TABLE ONLY public.sites
    ADD CONSTRAINT sites_floorplan_uploaded_by_user_id_fkey FOREIGN KEY (floorplan_uploaded_by_user_id) REFERENCES public.users(id) ON DELETE SET NULL;
ALTER TABLE ONLY public.space_capabilities
    ADD CONSTRAINT space_capabilities_criterion_id_fkey FOREIGN KEY (criterion_id) REFERENCES public.criteria(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.space_capabilities
    ADD CONSTRAINT space_capabilities_space_id_fkey FOREIGN KEY (space_id) REFERENCES public.spaces(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.spaces
    ADD CONSTRAINT spaces_group_id_fkey FOREIGN KEY (group_id) REFERENCES public.space_groups(id) ON DELETE SET NULL;
ALTER TABLE ONLY public.spaces
    ADD CONSTRAINT spaces_site_id_fkey FOREIGN KEY (site_id) REFERENCES public.sites(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.template_items
    ADD CONSTRAINT template_items_criterion_id_fkey FOREIGN KEY (criterion_id) REFERENCES public.criteria(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.template_items
    ADD CONSTRAINT template_items_template_id_fkey FOREIGN KEY (template_id) REFERENCES public.templates(id) ON DELETE CASCADE;
ALTER TABLE ONLY public.user_preferences
    ADD CONSTRAINT user_preferences_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

-- Seed default site
INSERT INTO sites (name, code)
SELECT 'Default Site', 'default'
WHERE NOT EXISTS (SELECT 1 FROM sites);
