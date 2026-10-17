export interface ChecklistTemplateItem {
  id: string;
  sortOrder: number;
  text: string;
  isMandatory: boolean;
}

/** GET /api/v1/checklist-templates */
export interface ChecklistTemplate {
  id: string;
  /** Null for a global template. */
  projectId: string | null;
  name: string;
  isActive: boolean;
  items: ChecklistTemplateItem[];
  canEdit: boolean;
  updatedAt: string;
  version: number;
}

export interface SaveChecklistTemplate {
  projectId: string | null;
  name: string;
  isActive: boolean;
  items: { text: string; isMandatory: boolean }[];
  version: number;
}
