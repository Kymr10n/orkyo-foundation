import { describe, it, expect, vi, beforeEach } from 'vitest';
import { handleError, withErrorHandler, getErrorMessage } from './error-handler';
import { logger } from './logger';

// Mock the logger to avoid console noise in tests
vi.mock('./logger', () => ({
  logger: {
    error: vi.fn(),
    warn: vi.fn(),
    info: vi.fn(),
    debug: vi.fn(),
  },
}));

const mockedLogger = vi.mocked(logger);

describe('error-handler', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('handleError', () => {
    it('returns the user message when provided', () => {
      const result = handleError(new Error('internal'), undefined, 'Something went wrong');
      expect(result).toBe('Something went wrong');
    });

    it('returns generic fallback when no user message is provided', () => {
      const result = handleError(new Error('internal'));
      expect(result).toBe('An unexpected error occurred. Please try again.');
    });

    it('handles non-Error objects', () => {
      const result = handleError('string error');
      expect(result).toBe('An unexpected error occurred. Please try again.');
    });

    it('handles null/undefined errors', () => {
      const result = handleError(null);
      expect(result).toBe('An unexpected error occurred. Please try again.');
    });

    it('logs with context component and operation', () => {
      handleError(new Error('db failed'), {
        component: 'UserService',
        operation: 'fetchUser',
      });
      expect(mockedLogger.error).toHaveBeenCalledWith(
        '[UserService] fetchUser failed:',
        'db failed',
        undefined
      );
    });

    it('logs with default component and operation when no context', () => {
      handleError(new Error('oops'));
      expect(mockedLogger.error).toHaveBeenCalledWith(
        '[App] Operation failed:',
        'oops',
        undefined
      );
    });

    it('includes context data in log call', () => {
      const data = { userId: '123' };
      handleError(new Error('fail'), { component: 'Auth', operation: 'login', data });
      expect(mockedLogger.error).toHaveBeenCalledWith(
        '[Auth] login failed:',
        'fail',
        data
      );
    });
  });

  describe('withErrorHandler', () => {
    it('returns the result on success', async () => {
      const fn = async (x: number) => x * 2;
      const wrapped = withErrorHandler(fn, { component: 'Test' });
      expect(await wrapped(5)).toBe(10);
    });

    it('throws a new Error with the handled message on failure', async () => {
      const fn = async () => {
        throw new Error('boom');
      };
      const wrapped = withErrorHandler(fn, { component: 'Test' }, 'Custom message');
      await expect(wrapped()).rejects.toThrow('Custom message');
    });

    it('throws with generic message when no user message provided', async () => {
      const fn = async () => {
        throw new Error('internal');
      };
      const wrapped = withErrorHandler(fn, { component: 'Test' });
      await expect(wrapped()).rejects.toThrow('An unexpected error occurred. Please try again.');
    });
  });

  describe('getErrorMessage', () => {
    it('extracts message from Error instances', () => {
      expect(getErrorMessage(new Error('test error'))).toBe('test error');
    });

    it('returns string errors directly', () => {
      expect(getErrorMessage('something broke')).toBe('something broke');
    });

    it('extracts message from objects with message property', () => {
      expect(getErrorMessage({ message: 'obj error' })).toBe('obj error');
    });

    it('returns fallback for unknown types', () => {
      expect(getErrorMessage(42)).toBe('An unknown error occurred');
      expect(getErrorMessage(null)).toBe('An unknown error occurred');
      expect(getErrorMessage(undefined)).toBe('An unknown error occurred');
    });

    it('handles objects without message property', () => {
      expect(getErrorMessage({ code: 500 })).toBe('An unknown error occurred');
    });
  });
});
