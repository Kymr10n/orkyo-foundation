import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getSecurityInfo,
  getSessions,
  changePassword,
  revokeSession,
  logoutAllSessions,
} from './security-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

const mockSecurityInfo = {
  isFederated: false,
  identityProvider: undefined,
  canChangePassword: true,
};

const mockSession = {
  id: 'session-123',
  ipAddress: '192.168.1.1',
  startTime: '2024-01-01T10:00:00Z',
  lastAccessTime: '2024-01-01T12:00:00Z',
  isCurrent: true,
  clients: ['web-app'],
};

describe('security-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('getSecurityInfo', () => {
    it('calls apiGet with correct endpoint', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockSecurityInfo);

      const result = await getSecurityInfo();

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.ACCOUNT.SECURITY_INFO);
      expect(result).toEqual(mockSecurityInfo);
    });

    it('returns federated user info when applicable', async () => {
      const federatedInfo = {
        isFederated: true,
        identityProvider: 'google',
        canChangePassword: false,
      };
      vi.mocked(apiClient.apiGet).mockResolvedValue(federatedInfo);

      const result = await getSecurityInfo();

      expect(result.isFederated).toBe(true);
      expect(result.canChangePassword).toBe(false);
    });
  });

  describe('getSessions', () => {
    it('calls apiGet with correct endpoint', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockSession]);

      const result = await getSessions();

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.ACCOUNT.SESSIONS);
      expect(result).toEqual([mockSession]);
    });

    it('returns multiple sessions', async () => {
      const sessions = [
        mockSession,
        { ...mockSession, id: 'session-456', isCurrent: false },
      ];
      vi.mocked(apiClient.apiGet).mockResolvedValue(sessions);

      const result = await getSessions();

      expect(result).toHaveLength(2);
    });
  });

  describe('changePassword', () => {
    it('calls apiPost with correct endpoint and data', async () => {
      const changePasswordRequest = {
        currentPassword: 'oldpass123',
        newPassword: 'newpass456',
        confirmPassword: 'newpass456',
      };
      vi.mocked(apiClient.apiPost).mockResolvedValue({ message: 'Password changed successfully' });

      const result = await changePassword(changePasswordRequest);

      expect(apiClient.apiPost).toHaveBeenCalledWith(
        API_PATHS.ACCOUNT.PASSWORD,
        changePasswordRequest
      );
      expect(result.message).toBe('Password changed successfully');
    });
  });

  describe('revokeSession', () => {
    it('calls apiDelete with correct endpoint', async () => {
      vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

      await revokeSession('session-123');

      expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.ACCOUNT.session('session-123'));
    });
  });

  describe('logoutAllSessions', () => {
    it('calls apiPost with correct endpoint', async () => {
      vi.mocked(apiClient.apiPost).mockResolvedValue({ message: 'All sessions terminated' });

      const result = await logoutAllSessions();

      expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.ACCOUNT.LOGOUT_ALL, undefined);
      expect(result.message).toBe('All sessions terminated');
    });
  });
});
