import { STORAGE_KEYS } from "@foundation/src/constants/storage";
import { create } from "zustand";
import { createJSONStorage, persist } from "zustand/middleware";

/**
 * Whether the visitor has hidden the shared-demo notice.
 *
 * The notice strip (`SharedDemoBanner`) and the "Demo" badge in the top bar both read it, so
 * hiding from one place and restoring from the other stay in step. It persists in
 * sessionStorage so a reload keeps the choice. sessionStorage outlives the demo session
 * itself, so the two places that end an ephemeral session (the 401 branch in `api-utils`
 * and `performLogin` in the auth machine) reset it before they send the visitor away.
 */
interface SharedDemoState {
  noticeHidden: boolean;
  setNoticeHidden: (hidden: boolean) => void;
}

/**
 * sessionStorage that never throws. The session-end paths write this store right before they
 * navigate the visitor away, and `persist` writes synchronously inside `set`, so a quota or
 * private-mode failure would otherwise propagate out of `setNoticeHidden` and skip the
 * redirect. Losing the preference is the acceptable outcome; a stranded visitor is not.
 */
const quietSessionStorage = {
  getItem: (name: string) => {
    try {
      return sessionStorage.getItem(name);
    } catch {
      return null;
    }
  },
  setItem: (name: string, value: string) => {
    try {
      sessionStorage.setItem(name, value);
    } catch {
      // Preference not saved; the live store still holds it for this page.
    }
  },
  removeItem: (name: string) => {
    try {
      sessionStorage.removeItem(name);
    } catch {
      // Nothing to do: there is no stored value to clear.
    }
  },
};

export const useSharedDemoStore = create<SharedDemoState>()(
  persist(
    (set) => ({
      noticeHidden: false,
      setNoticeHidden: (noticeHidden) => set({ noticeHidden }),
    }),
    {
      name: STORAGE_KEYS.SHARED_DEMO_NOTICE,
      storage: createJSONStorage(() => quietSessionStorage),
    },
  ),
);
