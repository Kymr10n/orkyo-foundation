-- Reverts 1880, approximately: re-creates the three 1300 rows with the flags later
-- migrations gave them (1660 icons, 1700 behaviour flags, 1750 plurals) and is_system=false
-- (the only value after 1870). Rows a tenant kept are untouched via ON CONFLICT.

INSERT INTO public.resource_types
    (key, display_name, display_name_plural, icon,
     is_system, is_active, has_geometry, has_directory_profile, single_group_membership)
VALUES
    ('space',  'Space',  'Spaces', 'Box',    false, true, true,  false, true),
    ('person', 'Person', 'People', 'Users',  false, true, false, true,  false),
    ('tool',   'Tool',   'Tools',  'Wrench', false, true, false, false, false)
ON CONFLICT (key) DO NOTHING;
