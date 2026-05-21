-- @migration-class: expand

-- Expand the off_times type check constraint to include HR-specific absence
-- types (vacation, sick_leave, unavailable, training, public_holiday, other).
-- Purely additive — existing rows are unaffected.

ALTER TABLE public.off_times
    DROP CONSTRAINT off_times_type_check;

ALTER TABLE public.off_times
    ADD CONSTRAINT off_times_type_check CHECK (
        type IN (
            'holiday',
            'maintenance',
            'custom',
            'vacation',
            'sick_leave',
            'unavailable',
            'training'
        )
    );
