import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient } from '@tanstack/react-query';
import { createFeedbackMutationCache } from './query-client';

const toastSuccess = vi.fn();
const toastError = vi.fn();
const toastImpl = { success: toastSuccess, error: toastError };

function makeClient() {
  const client: QueryClient = new QueryClient({
    defaultOptions: { mutations: { retry: false } },
    mutationCache: createFeedbackMutationCache(() => client, toastImpl),
  });
  return client;
}

async function runMutation(
  client: QueryClient,
  fn: () => Promise<unknown>,
  meta?: Record<string, unknown>,
) {
  const mutation = client
    .getMutationCache()
    .build(client, { mutationFn: fn, meta: meta as never });
  try {
    await mutation.execute(undefined);
  } catch {
    // swallow — error feedback is asserted via the cache, not the throw
  }
}

describe('createFeedbackMutationCache', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fires a success toast when meta.successMessage is set', async () => {
    const client = makeClient();
    await runMutation(client, () => Promise.resolve('ok'), { successMessage: 'Saved' });
    expect(toastSuccess).toHaveBeenCalledWith('Saved');
  });

  it('resolves a function successMessage with the mutation data and variables', async () => {
    const client = makeClient();
    const mutation = client.getMutationCache().build(client, {
      mutationFn: (email: string) => Promise.resolve({ ack: email }),
      meta: {
        successMessage: (data: unknown, variables: unknown) =>
          `Sent to ${variables} (ack ${(data as { ack: string }).ack})`,
      } as never,
    });
    await mutation.execute('a@b.com');
    expect(toastSuccess).toHaveBeenCalledWith('Sent to a@b.com (ack a@b.com)');
  });

  it('invalidates the meta.invalidates query keys on success', async () => {
    const client = makeClient();
    const spy = vi.spyOn(client, 'invalidateQueries');
    await runMutation(client, () => Promise.resolve('ok'), {
      successMessage: 'Saved',
      invalidates: [['widgets'], ['gadgets', 'x']],
    });
    expect(spy).toHaveBeenCalledWith({ queryKey: ['widgets'], exact: false });
    expect(spy).toHaveBeenCalledWith({ queryKey: ['gadgets', 'x'], exact: false });
  });

  it('does not toast on success when meta is absent', async () => {
    const client = makeClient();
    await runMutation(client, () => Promise.resolve('ok'));
    expect(toastSuccess).not.toHaveBeenCalled();
  });

  it('fires an error toast (with the error message as description) when opted in', async () => {
    const client = makeClient();
    await runMutation(client, () => Promise.reject(new Error('Boom')), {
      successMessage: 'Saved',
      errorMessage: 'Failed to save',
    });
    expect(toastError).toHaveBeenCalledWith('Failed to save', { description: 'Boom' });
  });

  it('uses a default error title when only successMessage is set', async () => {
    const client = makeClient();
    await runMutation(client, () => Promise.reject(new Error('Boom')), {
      successMessage: 'Saved',
    });
    expect(toastError).toHaveBeenCalledWith('Something went wrong', { description: 'Boom' });
  });

  it('does NOT fire an error toast when the mutation did not opt in (no meta)', async () => {
    const client = makeClient();
    await runMutation(client, () => Promise.reject(new Error('Boom')));
    expect(toastError).not.toHaveBeenCalled();
  });

  // Regression: a mutation that declares meta.errorMessage must get exactly one error
  // toast from this central cache — a caller that ALSO hand-rolls its own toast.error on
  // failure would double it. Guard both halves of that contract in one assertion.
  it('fires exactly one error toast for a failed mutation with meta.errorMessage', async () => {
    const client = makeClient();
    await runMutation(client, () => Promise.reject(new Error('Boom')), {
      errorMessage: 'Failed to save',
    });
    expect(toastError).toHaveBeenCalledTimes(1);
    expect(toastSuccess).not.toHaveBeenCalled();
  });
});
