import { useMemo } from 'react';
import { Navigate, Outlet, useNavigate, useParams } from 'react-router';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import { EmptyState } from '@foundation/src/components/ui/EmptyState';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import { useResourceCustomFields } from '@foundation/src/hooks/useResourceCustomFields';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';
import { usePageTitle } from '@foundation/src/hooks/usePageTitle';
import {
  CLASS_LABELS,
  RESOURCE_CLASS,
  classSegment,
  resourceClassOf,
  type ResourceClass,
} from '@foundation/src/constants/resource-class';
import { Boxes } from 'lucide-react';

interface ResourceClassPageProps {
  resourceClass: ResourceClass;
  /**
   * A surface of the class that belongs to no single type. `floorplan` is the only one: the plan
   * holds every placeable type at once, so it has one typeless URL rather than one per type, and
   * the type selector is hidden because a selector over a cross-type canvas selects nothing.
   */
  surface?: 'floorplan';
}

/**
 * One page for a whole class of resources — Stations or Assets — with the type as a selector
 * rather than as navigation.
 *
 * The point of the shape is that adding a resource type costs nothing: no sidebar entry, no route,
 * no tab. Whichever type is selected, the tabs stay in the same place, so a reader never has to
 * relearn where things are. The one exception is Lists, which appears only for a type that has
 * lists to show (progressive disclosure).
 */
export function ResourceClassPage({ resourceClass, surface }: ResourceClassPageProps) {
  const { typeKey } = useParams<{ typeKey: string }>();
  const navigate = useNavigate();
  const { data: types = [], isLoading } = useResourceTypes(true);
  // Tab is the third segment: /stations/<key>/<tab>. The floorplan's URL has no third segment to
  // read — it is /stations/floorplan — so its tab is named rather than derived.
  const derivedTab = useActiveTab('instances', 3);
  const active = surface ?? derivedTab;

  const segment = classSegment(resourceClass);
  const labels = CLASS_LABELS[resourceClass];

  const ofClass = useMemo(
    () => types.filter((t) => resourceClassOf(t) === resourceClass),
    [types, resourceClass],
  );
  // The floorplan is typeless, but the other tabs are not: they need a type to point at, and the
  // first of the class is where a bare /stations already lands.
  const resourceType = surface ? ofClass[0] : ofClass.find((t) => t.key === typeKey);

  // The Lists tab shows what this type's custom fields reach, so the fields decide whether it
  // exists at all. Loaded with the rest of the page, so the tab set does not flicker into view.
  const { data: customFields = [], isSuccess: fieldsLoaded } = useResourceCustomFields(
    resourceType?.id ?? '',
    !!resourceType,
  );
  const hasLists = customFields.some((f) => f.isActive && f.dataType === 'list_lookup');

  usePageTitle(surface === 'floorplan' ? 'Floorplan' : (resourceType?.displayNamePlural ?? labels.plural));

  if (isLoading) return <LoadingSpinner message="Loading…" />;

  // No type of this class at all — a tenant that defined no station, say. Nothing to select.
  if (ofClass.length === 0) {
    return (
      <PageLayout>
        <PageHeader title={labels.plural} description={DESCRIPTIONS[resourceClass]} />
        <EmptyState
          icon={<Boxes className="h-8 w-8" />}
          message={`No ${labels.plural.toLowerCase()} are defined yet. An administrator activates pre-configured types under Configuration → Type catalog, or adds custom ones under Resource Types.`}
        />
      </PageLayout>
    );
  }

  // No type in the URL, or one that belongs to the other class: land on the first of this class.
  if (!resourceType) {
    return <Navigate to={`/${segment}/${ofClass[0].key}/instances`} replace />;
  }

  // Nothing to show under Lists for this type, so the tab that named it must not stay selected —
  // a deep link, or a type change made while it was open, would otherwise leave the strip with no
  // active trigger over an empty panel.
  if (active === 'lists' && fieldsLoaded && !hasLists) {
    return <Navigate to={`/${segment}/${resourceType.key}/instances`} replace />;
  }

  const tabs: PageTab[] = [
    // Named for the class, not the selected type: the type is already stated by the selector
    // beside it, and a tab that renamed itself made the strip look like a different page.
    { value: 'instances', label: labels.plural },
    { value: 'groups', label: 'Groups' },
    ...(hasLists ? [{ value: 'lists', label: 'Lists' }] : []),
    // The plan is a station surface of its own — cross-type and site-scoped — so it is reached
    // from here rather than owned by the selected type.
    ...(resourceClass === RESOURCE_CLASS.STATION
      ? [{ value: 'floorplan', label: 'Floorplan' }]
      : []),
  ];

  return (
    <PageLayout>
      <PageHeader
        title={labels.plural}
        description={surface === 'floorplan' ? FLOORPLAN_DESCRIPTION : (resourceType.description || DESCRIPTIONS[resourceClass])}
        actions={
          // Hidden on the floorplan: it draws every placeable type at once, so a control that
          // picks one of them would do nothing to what is on screen.
          surface ? undefined : (
          <div className="w-56">
            <Select
              value={resourceType.key}
              // Lists is not carried across: the target type need not have any, and landing on a
              // tab it does not offer is worse than landing on its instances.
              onValueChange={(key) =>
                navigate(`/${segment}/${key}/${active === 'lists' ? 'instances' : active}`)
              }
            >
              <SelectTrigger aria-label="Type">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {ofClass.map((t) => (
                  <SelectItem key={t.key} value={t.key}>
                    {t.displayNamePlural}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          )
        }
      />
      <PageTabs
        tabs={tabs}
        value={active}
        onChange={(v) =>
          navigate(v === 'floorplan' ? `/${segment}/floorplan` : `/${segment}/${resourceType.key}/${v}`)
        }
      >
        <Outlet context={{ resourceType }} />
      </PageTabs>
    </PageLayout>
  );
}

const FLOORPLAN_DESCRIPTION = 'Place and arrange every station on the site plan';

const DESCRIPTIONS: Record<ResourceClass, string> = {
  station: 'Resources with a fixed location, placeable on a floorplan',
  asset: 'Mobile resources, planned by availability',
};
