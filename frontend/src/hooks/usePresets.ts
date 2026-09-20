import { useMutation, useQuery } from "@tanstack/react-query";
import {
  applyPreset,
  exportPreset,
  getPresetApplications,
  validatePreset,
  type Preset,
  type PresetApplicationResult,
  type PresetValidationResult,
} from "@foundation/src/lib/api/preset-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { RESOURCE_TYPE_KEY } from "@foundation/src/constants/resource-type-key";

/** The preset application history for this tenant. */
export const usePresetApplications = () =>
  useQuery({
    queryKey: qk.presetApplications.all(),
    queryFn: getPresetApplications,
  });

/** Dry-run check of an imported preset file. Its result is the in-dialog feedback. */
export const useValidatePreset = (onSuccess: (result: PresetValidationResult) => void) =>
  useMutation({
    mutationFn: (preset: Preset) => validatePreset(preset),
    onSuccess,
  });

/**
 * No successMessage: the response carries its own success flag and the in-dialog
 * application-result panel is the feedback; a central toast could claim success on a
 * partially-failed application. Invalidation is harmless either way, so it lives in
 * meta per docs/dialog-feedback.md.
 */
export const useApplyPreset = (onSuccess: (result: PresetApplicationResult) => void) =>
  useMutation({
    mutationFn: (preset: Preset) => applyPreset(preset),
    meta: {
      invalidates: [
        qk.presetApplications.all(),
        qk.criteria.all(),
        // Presets write space groups; their queries live under the
        // resource-groups key for the space type.
        qk.resourceGroups.byType(RESOURCE_TYPE_KEY.SPACE),
        qk.templates("request"),
        qk.templates("space"),
        qk.templates("group"),
      ],
    },
    onSuccess,
  });

export const useExportPreset = (onSuccess: (preset: Preset) => void) =>
  useMutation({
    mutationFn: ({ presetId, name, description }: { presetId: string; name: string; description?: string }) =>
      exportPreset(presetId, name, description),
    onSuccess,
  });
