import { RESOURCE_TYPE_KEY } from '@foundation/src/constants/resource-type-key';
import { ResourceCapabilitiesEditor } from '../resources/ResourceCapabilitiesEditor';

interface PersonSkillsEditorProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  resourceId: string;
  resourceName: string;
}

/**
 * Per-person skill assignment. People call their criterion values "skills"; the mechanism
 * is the shared one every resource type uses, so this is only naming and a type key.
 */
export function PersonSkillsEditor({
  open,
  onOpenChange,
  resourceId,
  resourceName,
}: PersonSkillsEditorProps) {
  return (
    <ResourceCapabilitiesEditor
      open={open}
      onOpenChange={onOpenChange}
      resourceId={resourceId}
      resourceName={resourceName}
      resourceTypeKey={RESOURCE_TYPE_KEY.PERSON}
      valueLabel={{ plural: 'Skills', singular: 'Skill' }}
      entityLabel="person"
    />
  );
}
