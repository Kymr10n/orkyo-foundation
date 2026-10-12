import { STORAGE_KEYS } from "@foundation/src/constants/storage";
import { create } from "zustand";
import { createJSONStorage, persist } from "zustand/middleware";

/**
 * Whether the visitor has hidden the shared-demo notice.
 *
 * The notice strip (`SharedDemoBanner`) and the "Demo" badge in the top bar both read it, so
 * hiding from one place and restoring from the other stay in step. It persists in
 * sessionStorage, the same lifetime as the ephemeral-session flag it accompanies: a reload
 * keeps the choice, a new demo session starts with the notice shown.
 */
interface SharedDemoState {
  noticeHidden: boolean;
  setNoticeHidden: (hidden: boolean) => void;
}

export const useSharedDemoStore = create<SharedDemoState>()(
  persist(
    (set) => ({
      noticeHidden: false,
      setNoticeHidden: (noticeHidden) => set({ noticeHidden }),
    }),
    {
      name: STORAGE_KEYS.SHARED_DEMO_NOTICE,
      storage: createJSONStorage(() => sessionStorage),
    },
  ),
);
