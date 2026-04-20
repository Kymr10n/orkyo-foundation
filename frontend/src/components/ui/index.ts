/**
 * UI Components Barrel Export
 *
 * Central export point for all UI components.
 * Import from '@/components/ui' instead of individual files.
 *
 * Example:
 *   import { Button, Badge, Select, SelectContent } from '@/components/ui';
 */

// Alert Dialog
export {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "./alert-dialog";

// Alert
export { Alert, AlertDescription, AlertTitle } from "./alert";

// Badge
export { Badge } from "./badge";

// Button
export { Button, buttonVariants } from "./button";

// Calendar
export { Calendar } from "./calendar";

// Card
export {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "./card";

// Checkbox
export { Checkbox } from "./checkbox";

// Collapsible
export {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "./collapsible";

// DateTimePicker
export { DateTimePicker } from "./date-time-picker";

// TimePicker
export { TimePicker } from "./time-picker";

// Dialog
export {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "./dialog";

// Input
export { Input } from "./input";

// Label
export { Label } from "./label";

// Popover
export { Popover, PopoverContent, PopoverTrigger } from "./popover";

// Scroll Area
export { ScrollArea } from "./scroll-area";

// Select
export {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "./select";

// Separator
export { Separator } from "./separator";

// Switch
export { Switch } from "./switch";

// Table
export {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "./table";

// Tabs
export { Tabs, TabsContent, TabsList, TabsTrigger } from "./tabs";

// Textarea
export { Textarea } from "./textarea";

// Tooltip
export {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "./tooltip";

// Visually Hidden
export { VisuallyHidden } from "./visually-hidden";

// Form utilities
export { DialogFormFooter } from "./DialogFormFooter";
export { ErrorAlert } from "./ErrorAlert";
