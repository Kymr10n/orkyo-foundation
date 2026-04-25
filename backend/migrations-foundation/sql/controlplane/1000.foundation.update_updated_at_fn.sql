-- Shared trigger function: stamps NEW.updated_at = now() on row update.
-- Used by tables in both control-plane and tenant databases.
CREATE OR REPLACE FUNCTION public.update_updated_at_column() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
	NEW.updated_at = NOW();
	RETURN NEW;
END;
$$;
