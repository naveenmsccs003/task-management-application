/** Attachments (docs/api-design.md §4.9). */
export type AttachmentOwnerType = 'Task' | 'Comment' | 'Algorithm' | 'Review';
export type AttachmentScanStatus = 'Pending' | 'Clean' | 'Infected' | 'Failed';
export type FileCategory = 'Image' | 'Document' | 'Video';

export interface Attachment {
  id: string;
  ownerType: AttachmentOwnerType;
  ownerId: string | null;
  fileName: string;
  contentType: string;
  fileType: string;
  category: FileCategory;
  sizeBytes: number;
  scanStatus: AttachmentScanStatus;
  uploadedBy: { id: string; name: string } | null;
  uploadedAt: string;
  canPreview: boolean;
  canDelete: boolean;
}

export interface AttachmentType {
  code: string;
  category: FileCategory;
  extensions: string[];
  maxBytes: number;
}

export interface AttachmentRules {
  allowedTypes: AttachmentType[];
  maxFilesPerRecord: number;
}

/** Every type the server can recognize (FileTypes.All); SVG and HTML are deliberately absent. */
export const FILE_TYPE_CATALOG: { code: string; label: string; category: FileCategory }[] = [
  { code: 'png', label: 'PNG', category: 'Image' },
  { code: 'jpeg', label: 'JPEG', category: 'Image' },
  { code: 'webp', label: 'WebP', category: 'Image' },
  { code: 'gif', label: 'GIF', category: 'Image' },
  { code: 'pdf', label: 'PDF', category: 'Document' },
  { code: 'docx', label: 'Word (DOCX)', category: 'Document' },
  { code: 'xlsx', label: 'Excel (XLSX)', category: 'Document' },
  { code: 'pptx', label: 'PowerPoint (PPTX)', category: 'Document' },
  { code: 'txt', label: 'Text (TXT)', category: 'Document' },
  { code: 'md', label: 'Markdown (MD)', category: 'Document' },
  { code: 'csv', label: 'CSV', category: 'Document' },
  { code: 'zip', label: 'ZIP', category: 'Document' },
  { code: 'mp4', label: 'MP4', category: 'Video' },
  { code: 'webm', label: 'WebM', category: 'Video' },
];

export const SCAN_STATUS_LABELS: Record<AttachmentScanStatus, string> = {
  Pending: 'Checking for malware…',
  Clean: 'Ready',
  Infected: 'Blocked: malware found',
  Failed: 'Blocked: could not be checked',
};

/** 1536 → "1.5 KB". */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB'];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${value < 10 ? value.toFixed(1) : Math.round(value)} ${units[unit]}`;
}

/**
 * Checks a chosen file against the rules before uploading, for a quick message. The server checks again by content
 * (FR-ATT-03); this is only a convenience.
 */
export function checkFile(file: { name: string; size: number }, rules: AttachmentRules): string | null {
  const dot = file.name.lastIndexOf('.');
  const extension = dot >= 0 ? file.name.slice(dot).toLowerCase() : '';
  const type = rules.allowedTypes.find((t) => t.extensions.includes(extension));
  if (!type) return `“${file.name}” is not an allowed file type.`;
  if (file.size === 0) return `“${file.name}” is empty.`;
  if (file.size > type.maxBytes) return `“${file.name}” is too large. The limit for this type is ${formatFileSize(type.maxBytes)}.`;
  return null;
}

/** The accept attribute for a file input: every allowed extension. */
export function acceptList(rules: AttachmentRules): string {
  return rules.allowedTypes.flatMap((t) => t.extensions).join(',');
}
