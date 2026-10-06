import { describe, it, expect, vi } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { toast } from 'sonner';
import * as securityApi from '@foundation/src/lib/api/security-api';
import { createTestQueryClient } from '@foundation/src/test-utils';
import * as tenantAccountApi from '@foundation/src/lib/api/tenant-account-api';
import {
  useDeleteOwnAccount,
  useDeleteTenant,
  useExportPersonalData,
  useRequestEmailChange,
  useTenantMemberships,
} from './useAccount';
import { downloadFile } from '@foundation/src/lib/utils/import-export';

vi.mock('@foundation/src/lib/api/security-api');
vi.mock('@foundation/src/lib/api/tenant-account-api');
vi.mock('@foundation/src/lib/utils/import-export', () => ({ downloadFile: vi.fn() }));

describe('useExportPersonalData', () => {
  it('toasts when the export cannot be prepared and downloads nothing', async () => {
    vi.mocked(tenantAccountApi.exportPersonalData).mockRejectedValue(new Error('Server down'));
    const { wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useExportPersonalData(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync().catch(() => {});
    });

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('Could not prepare your data export', { description: 'Server down' }),
    );
    expect(downloadFile).not.toHaveBeenCalled();
  });
});

describe('useDeleteOwnAccount', () => {
  it('passes the typed email through and reports a failure inline only', async () => {
    vi.mocked(tenantAccountApi.deleteOwnAccount).mockRejectedValue(new Error('You are the only admin of ACME.'));
    const { wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useDeleteOwnAccount(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync('alex@example.com').catch(() => {});
    });

    expect(tenantAccountApi.deleteOwnAccount).toHaveBeenCalledWith('alex@example.com');
    await waitFor(() => expect(result.current.error?.message).toBe('You are the only admin of ACME.'));
    expect(toast.error).not.toHaveBeenCalled();
  });
});

describe('useRequestEmailChange', () => {
  it('reports a failure inline only: no error toast next to the alert', async () => {
    vi.mocked(securityApi.requestEmailChange).mockRejectedValue(new Error('Email already in use'));
    const { wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useRequestEmailChange(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync('new@example.com').catch(() => {});
    });

    await waitFor(() => expect(result.current.error?.message).toBe('Email already in use'));
    expect(toast.error).not.toHaveBeenCalled();
  });
});

describe('useDeleteTenant', () => {
  it('reports a failure inline only when called without meta', async () => {
    vi.mocked(tenantAccountApi.deleteTenant).mockRejectedValue(new Error('Grace period active'));
    const { wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useDeleteTenant(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync('t-1').catch(() => {});
    });

    expect(tenantAccountApi.deleteTenant).toHaveBeenCalledWith('t-1');
    await waitFor(() => expect(result.current.error?.message).toBe('Grace period active'));
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('toasts the given errorMessage when called with meta', async () => {
    vi.mocked(tenantAccountApi.deleteTenant).mockRejectedValue(new Error('Grace period active'));
    const { wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useDeleteTenant({ errorMessage: 'Could not delete' }), { wrapper });

    await act(async () => {
      await result.current.mutateAsync('t-1').catch(() => {});
    });

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('Could not delete', { description: 'Grace period active' }),
    );
  });
});

describe('useTenantMemberships', () => {
  it('clears the previous load failure once a reload succeeds', async () => {
    vi.mocked(tenantAccountApi.getTenantMemberships)
      .mockRejectedValueOnce(new Error('Network down'))
      .mockResolvedValueOnce([{ tenantId: 't-1' } as never]);
    const { result } = renderHook(() => useTenantMemberships());

    await waitFor(() => expect(result.current.error).toBe('Network down'));
    expect(result.current.loading).toBe(false);

    await act(() => result.current.reload());

    expect(result.current.error).toBeNull();
    expect(result.current.memberships).toEqual([{ tenantId: 't-1' }]);
  });
});
