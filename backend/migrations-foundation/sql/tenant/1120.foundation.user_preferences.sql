CREATE TABLE public.user_preferences (
    user_id     uuid                     NOT NULL,
    preferences jsonb                    DEFAULT '{}'::jsonb NOT NULL,
    created_at  timestamp with time zone DEFAULT now() NOT NULL,
    updated_at  timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT user_preferences_pkey PRIMARY KEY (user_id),
    CONSTRAINT user_preferences_user_id_fkey
        FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE
);

CREATE INDEX idx_user_preferences_user_id ON public.user_preferences USING btree (user_id);
