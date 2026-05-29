import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@foundation/src/components/ui/select";
import { Switch } from "@foundation/src/components/ui/switch";
import type { Criterion, CriterionValue } from "@foundation/src/types/criterion";

const NUMBER_OPERATORS = [
  { value: ">=", label: "≥ at least" },
  { value: "<=", label: "≤ at most" },
  { value: "=",  label: "= exactly" },
] as const;

interface CriterionRequirementInputProps {
  criterion: Criterion;
  value: CriterionValue | null;
  operator?: string;
  onChange: (value: CriterionValue | null) => void;
  onOperatorChange?: (operator: string) => void;
  label?: string;
}

export function CriterionRequirementInput({
  criterion,
  value,
  operator,
  onChange,
  onOperatorChange,
  label,
}: CriterionRequirementInputProps) {
  const renderInput = () => {
    switch (criterion.dataType) {
      case "Boolean":
        return (
          <div className="flex items-center gap-2">
            <Switch
              checked={value === true}
              onCheckedChange={onChange}
              id={`criterion-${criterion.id}`}
            />
            <Label
              htmlFor={`criterion-${criterion.id}`}
              className="text-sm cursor-pointer"
            >
              {value ? "Yes" : "No"}
            </Label>
          </div>
        );

      case "Number":
        return (
          <div className="flex items-center gap-2">
            <Select
              value={operator ?? ">="}
              onValueChange={(v) => onOperatorChange?.(v)}
            >
              <SelectTrigger className="w-32 shrink-0">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {NUMBER_OPERATORS.map((op) => (
                  <SelectItem key={op.value} value={op.value}>
                    {op.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Input
              type="text"
              inputMode="decimal"
              value={(value as string | number) ?? ""}
              onChange={(e) => {
                const val = e.target.value;
                const cleaned = val.replace(/[^0-9.-]/g, '');
                onChange(cleaned === "" ? null : parseFloat(cleaned) || null);
              }}
              placeholder={`Enter ${criterion.name.toLowerCase()}`}
              className="flex-1"
            />
            {criterion.unit && (
              <span className="text-sm text-muted-foreground">{criterion.unit}</span>
            )}
          </div>
        );

      case "String":
        return (
          <Input
            type="text"
            value={(value as string) ?? ""}
            onChange={(e) => onChange(e.target.value || null)}
            placeholder={`Enter ${criterion.name.toLowerCase()}`}
          />
        );

      case "Enum":
        return (
          <Select value={(value as string) ?? ""} onValueChange={(v) => onChange(v || null)}>
            <SelectTrigger>
              <SelectValue placeholder={`Select ${criterion.name.toLowerCase()}`} />
            </SelectTrigger>
            <SelectContent>
              {criterion.enumValues?.map((option) => (
                <SelectItem key={option} value={option}>
                  {option}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        );

      default:
        return <Input disabled value="Unsupported type" />;
    }
  };

  return (
    <div className="space-y-2">
      <Label className="text-sm font-medium">
        {label || criterion.name}
        {criterion.description && (
          <span className="text-muted-foreground font-normal ml-2">
            ({criterion.description})
          </span>
        )}
      </Label>
      {renderInput()}
    </div>
  );
}
