import { describe, it, expect } from 'vitest';
import { API_PATHS } from './api-paths';

describe('api-paths', () => {
  describe('SESSION paths', () => {
    it('has correct bootstrap path', () => {
      expect(API_PATHS.SESSION.BOOTSTRAP).toBe('/api/session/bootstrap');
    });

    it('has correct me path', () => {
      expect(API_PATHS.SESSION.ME).toBe('/api/session/me');
    });

    it('has correct tos accept path', () => {
      expect(API_PATHS.SESSION.TOS_ACCEPT).toBe('/api/session/tos/accept');
    });
  });

  describe('SEARCH path', () => {
    it('has correct search path', () => {
      expect(API_PATHS.SEARCH).toBe('/api/search');
    });
  });

  describe('Sites paths', () => {
    it('has correct sites collection path', () => {
      expect(API_PATHS.SITES).toBe('/api/sites');
    });

    it('generates correct site path', () => {
      expect(API_PATHS.site('site-123')).toBe('/api/sites/site-123');
    });

    it('generates correct site floorplan path', () => {
      expect(API_PATHS.siteFloorplan('site-123')).toBe('/api/sites/site-123/floorplan');
    });

    it('generates correct site floorplan metadata path', () => {
      expect(API_PATHS.siteFloorplanMetadata('site-123')).toBe('/api/sites/site-123/floorplan/metadata');
    });
  });

  describe('Spaces paths', () => {
    it('generates correct spaces collection path', () => {
      expect(API_PATHS.spaces('site-123')).toBe('/api/sites/site-123/spaces');
    });

    it('generates correct space path', () => {
      expect(API_PATHS.space('site-123', 'space-456')).toBe('/api/sites/site-123/spaces/space-456');
    });

    it('generates correct space capabilities path', () => {
      expect(API_PATHS.spaceCapabilities('site-123', 'space-456'))
        .toBe('/api/sites/site-123/spaces/space-456/capabilities');
    });

    it('generates correct space capability path', () => {
      expect(API_PATHS.spaceCapability('site-123', 'space-456', 'cap-789'))
        .toBe('/api/sites/site-123/spaces/space-456/capabilities/cap-789');
    });
  });

  describe('Groups paths', () => {
    it('has correct groups collection path', () => {
      expect(API_PATHS.GROUPS).toBe('/api/groups');
    });

    it('generates correct group path', () => {
      expect(API_PATHS.group('group-123')).toBe('/api/groups/group-123');
    });

    it('generates correct group capabilities path', () => {
      expect(API_PATHS.groupCapabilities('group-123')).toBe('/api/groups/group-123/capabilities');
    });

    it('generates correct group capability path', () => {
      expect(API_PATHS.groupCapability('group-123', 'cap-456'))
        .toBe('/api/groups/group-123/capabilities/cap-456');
    });
  });

  describe('Requests paths', () => {
    it('has correct requests collection path', () => {
      expect(API_PATHS.REQUESTS).toBe('/api/requests');
    });

    it('generates correct request path', () => {
      expect(API_PATHS.request('req-123')).toBe('/api/requests/req-123');
    });

    it('generates correct request schedule path', () => {
      expect(API_PATHS.requestSchedule('req-123')).toBe('/api/requests/req-123/schedule');
    });

    it('generates correct request requirements path', () => {
      expect(API_PATHS.requestRequirements('req-123')).toBe('/api/requests/req-123/requirements');
    });

    it('generates correct request requirement path', () => {
      expect(API_PATHS.requestRequirement('req-123', 'rreq-456'))
        .toBe('/api/requests/req-123/requirements/rreq-456');
    });
  });

  describe('Templates paths', () => {
    it('has correct templates collection path', () => {
      expect(API_PATHS.TEMPLATES).toBe('/api/templates');
    });

    it('generates correct templates with type path', () => {
      expect(API_PATHS.templatesWithType('request')).toBe('/api/templates?entityType=request');
      expect(API_PATHS.templatesWithType('space')).toBe('/api/templates?entityType=space');
      expect(API_PATHS.templatesWithType('group')).toBe('/api/templates?entityType=group');
    });

    it('generates correct template path', () => {
      expect(API_PATHS.template('tmpl-123')).toBe('/api/templates/tmpl-123');
    });
  });

  describe('Criteria paths', () => {
    it('has correct criteria collection path', () => {
      expect(API_PATHS.CRITERIA).toBe('/api/criteria');
    });

    it('generates correct criterion path', () => {
      expect(API_PATHS.criterion('crit-123')).toBe('/api/criteria/crit-123');
    });
  });

  describe('Users paths', () => {
    it('has correct users collection path', () => {
      expect(API_PATHS.USERS).toBe('/api/users');
    });

    it('has correct user invitations path', () => {
      expect(API_PATHS.USER_INVITATIONS).toBe('/api/users/invitations');
    });

    it('has correct user invite path', () => {
      expect(API_PATHS.USER_INVITE).toBe('/api/users/invite');
    });

    it('generates correct user path', () => {
      expect(API_PATHS.user('user-123')).toBe('/api/users/user-123');
    });

    it('generates correct user role path', () => {
      expect(API_PATHS.userRole('user-123')).toBe('/api/users/user-123/role');
    });

    it('generates correct user invitation path', () => {
      expect(API_PATHS.userInvitation('inv-123')).toBe('/api/users/invitations/inv-123');
    });

    it('generates correct user invitation resend path', () => {
      expect(API_PATHS.userInvitationResend('inv-123')).toBe('/api/users/invitations/inv-123/resend');
    });
  });

  describe('Feedback path', () => {
    it('has correct feedback path', () => {
      expect(API_PATHS.FEEDBACK).toBe('/api/feedback');
    });
  });

  describe('TENANTS paths', () => {
    it('has correct can-create path', () => {
      expect(API_PATHS.TENANTS.CAN_CREATE).toBe('/api/tenants/can-create');
    });

    it('has correct create path', () => {
      expect(API_PATHS.TENANTS.CREATE).toBe('/api/tenants');
    });

    it('has correct memberships path', () => {
      expect(API_PATHS.TENANTS.MEMBERSHIPS).toBe('/api/tenants/memberships');
    });

    it('generates correct leave path', () => {
      expect(API_PATHS.TENANTS.leave('tenant-123')).toBe('/api/tenants/tenant-123/leave');
    });

    it('generates correct delete path', () => {
      expect(API_PATHS.TENANTS.delete('tenant-abc')).toBe('/api/tenants/tenant-abc');
    });
  });

  describe('ACCOUNT paths', () => {
    it('has correct password path', () => {
      expect(API_PATHS.ACCOUNT.PASSWORD).toBe('/api/account/password');
    });

    it('has correct sessions path', () => {
      expect(API_PATHS.ACCOUNT.SESSIONS).toBe('/api/account/sessions');
    });

    it('has correct logout all path', () => {
      expect(API_PATHS.ACCOUNT.LOGOUT_ALL).toBe('/api/account/logout-all');
    });

    it('has correct security info path', () => {
      expect(API_PATHS.ACCOUNT.SECURITY_INFO).toBe('/api/account/security-info');
    });

    it('generates correct session path', () => {
      expect(API_PATHS.ACCOUNT.session('sess-123')).toBe('/api/account/sessions/sess-123');
    });
  });

  describe('ADMIN paths', () => {
    it('has correct presets validate path', () => {
      expect(API_PATHS.ADMIN.PRESETS_VALIDATE).toBe('/api/admin/presets/validate');
    });

    it('has correct presets apply path', () => {
      expect(API_PATHS.ADMIN.PRESETS_APPLY).toBe('/api/admin/presets/apply');
    });

    it('has correct presets export path', () => {
      expect(API_PATHS.ADMIN.PRESETS_EXPORT).toBe('/api/admin/presets/export');
    });

    it('has correct presets applications path', () => {
      expect(API_PATHS.ADMIN.PRESETS_APPLICATIONS).toBe('/api/admin/presets/applications');
    });
  });
});
