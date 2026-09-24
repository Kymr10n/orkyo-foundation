import type { ReactNode } from 'react';
import { useNavigate } from 'react-router';
import { AlertTriangle, CalendarClock, CalendarOff, Gauge, type LucideIcon, Pencil } from 'lucide-react';
import { Badge } from '@foundation/src/components/ui/badge';
import { Button } from '@foundation/src/components/ui/button';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@foundation/src/components/ui/sheet';
import { typeRoute } from '@foundation/src/constants/resource-class';
import { useBreakpoint } from '@foundation/src/hooks/useBreakpoint';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { useResourceStatus } from '@foundation/src/hooks/useResourceScanCodes';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import type { ResourceStatusBooking } from '@foundation/src/lib/api/resource-status-api';
import { formatDateTimeShort, formatPeriod } from '@foundation/src/lib/formatters';
import { useUiActionsStore } from '@foundation/src/store/ui-actions-store';

function StatRow({ icon: Icon, label, children }: { icon: LucideIcon; label: string; children: ReactNode }) {
  return (
    <div className="flex gap-3">
      <Icon className="text-muted-foreground mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
      <div>
        <dt className="text-muted-foreground text-xs">{label}</dt>
        <dd className="text-sm">{children}</dd>
      </div>
    </div>
  );
}

function BookingLine({ booking, empty }: { booking?: ResourceStatusBooking | null; empty: string }) {
  if (!booking) return <span className="text-muted-foreground">{empty}</span>;
  return (
    <>
      <span className="font-medium">{booking.requestName || 'Untitled request'}</span>
      <span className="text-muted-foreground block">{formatPeriod(booking.startUtc, booking.endUtc)}</span>
    </>
  );
}

/**
 * One resource at a glance (docs/qr-resource-linking-spec.md §7) — what a QR scan opens.
 * Read-only for every role: a scan is usually a lookup. Editors get an Edit button that
 * deep-links to the ordinary edit dialog. Mounted once in AppLayout.
 */
export function ResourceStatusSheet() {
  const resourceId = useUiActionsStore((s) => s.statusResourceId);
  const close = useUiActionsStore((s) => s.closeResourceStatus);
  const { isPhone } = useBreakpoint();
  const canEdit = useCanEdit();
  const navigate = useNavigate();
  const { data: status, isLoading, isError } = useResourceStatus(resourceId ?? '', !!resourceId);
  const { data: types } = useResourceTypes();
  const type = types?.find((t) => t.key === status?.resourceTypeKey);

  const edit = () => {
    if (!status || !type) return;
    close();
    navigate(`${typeRoute(type)}?edit=${status.resourceId}`);
  };

  return (
    <Sheet open={!!resourceId} onOpenChange={(open) => !open && close()}>
      <SheetContent side={isPhone ? 'bottom' : 'right'} className="flex flex-col gap-4 overflow-y-auto">
        <SheetHeader>
          <SheetTitle className="flex items-center gap-2">
            {status?.name ?? 'Resource'}
            {status && !status.isActive && <Badge variant="secondary">Inactive</Badge>}
          </SheetTitle>
          <SheetDescription>{type ? type.displayName : 'Current status'}</SheetDescription>
        </SheetHeader>

        {isLoading && <LoadingSpinner fullScreen={false} size="sm" muted className="py-6" />}
        {isError && <ErrorAlert message="The status of this resource could not be loaded." />}

        {status && (
          <dl className="space-y-4 px-4">
            <StatRow icon={CalendarClock} label="Now">
              <BookingLine booking={status.current} empty="Not booked" />
            </StatRow>
            <StatRow icon={CalendarClock} label="Next">
              <BookingLine booking={status.next} empty={`No booking in the next ${status.lookAheadDays} days`} />
            </StatRow>
            <StatRow icon={CalendarOff} label="Absence">
              {status.activeAbsence
                ? `${status.activeAbsence.title} until ${formatDateTimeShort(status.activeAbsence.endTs)}`
                : 'Available'}
            </StatRow>
            <StatRow icon={AlertTriangle} label={`Bookings with conflicts, next ${status.lookAheadDays} days`}>
              {status.conflictCount === 0 ? 'None' : status.conflictCount}
            </StatRow>
            <StatRow icon={Gauge} label={`Utilization, last ${status.utilizationDays} days`}>
              {status.utilizationPercent == null ? 'No data' : `${status.utilizationPercent}%`}
            </StatRow>
          </dl>
        )}

        {/* Editors only: the resource list opens the edit dialog for Editors only. */}
        {status && type && canEdit && (
          <SheetFooter className="mt-auto p-4">
            <Button onClick={edit}>
              <Pencil className="mr-2 h-4 w-4" />
              Edit
            </Button>
          </SheetFooter>
        )}
      </SheetContent>
    </Sheet>
  );
}
