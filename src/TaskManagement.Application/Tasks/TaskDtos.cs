using TaskManagement.Application.Common;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Tasks;

public sealed record UserRef(Guid Id, string Name);

/// <summary>Flags shown next to the state (task-workflow.md §1). More flags arrive with later phases.</summary>
public sealed record TaskFlagsDto(bool Overdue, bool OverAllocated, bool ExtensionPending, bool ReassignmentPending);

/// <summary>Checklist progress shown on the task (FR-CHK-05 needs every mandatory item done before review).</summary>
public sealed record ChecklistSummaryDto(int Total, int Done, int MandatoryTotal, int MandatoryDone);

/// <summary>GET /tasks/{id}. <see cref="AllowedActions"/> is computed with the same rules the server enforces.</summary>
public sealed record TaskDto(
    Guid Id,
    string Key,
    Guid ProjectId,
    string ProjectKey,
    string ProjectName,
    bool ProjectReadOnly,
    string Title,
    string Description,
    TaskType Type,
    TaskPriority Priority,
    TaskItemStatus Status,
    UserRef? Assignee,
    DateOnly? StartDate,
    DateOnly? DueDate,
    DateOnly? OriginalDueDate,
    int? AllocatedMinutes,
    long SpentSeconds,
    bool AlgorithmRequired,
    bool AlgorithmApproved,
    IReadOnlyList<string> Labels,
    int CurrentRevision,
    int? ApprovedRevision,
    TaskFlagsDto Flags,
    ChecklistSummaryDto Checklist,
    string? CancelReason,
    UserRef CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> AllowedActions,
    bool CanViewAlgorithm,
    bool CanViewTime,
    bool CanViewDelays,
    uint Version);

public sealed record TaskListItemDto(
    Guid Id,
    string Key,
    Guid ProjectId,
    string ProjectKey,
    string Title,
    TaskType Type,
    TaskPriority Priority,
    TaskItemStatus Status,
    UserRef? Assignee,
    DateOnly? DueDate,
    bool Overdue,
    IReadOnlyList<string> Labels,
    DateTimeOffset UpdatedAt);

/// <summary>FR-TASK-07 filters. <see cref="Sort"/> is a comma list of allow-listed fields; a leading minus sorts descending.</summary>
public sealed record TaskListQuery(
    Guid? ProjectId,
    IReadOnlyList<TaskItemStatus>? Statuses,
    Guid? AssigneeId,
    IReadOnlyList<TaskPriority>? Priorities,
    DateOnly? DueFrom,
    DateOnly? DueTo,
    bool? Overdue,
    string? Q,
    string? Sort,
    PageRequest Page);

/// <summary>
/// POST /tasks or /projects/{projectId}/tasks. Type and priority default to Feature and Medium; "algorithm required"
/// defaults from the project (FR-TASK-04).
/// </summary>
public sealed record CreateTaskCommand(
    Guid? ProjectId,
    string? Title,
    string? Description,
    TaskType? Type,
    TaskPriority? Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    int? AllocatedMinutes,
    bool? AlgorithmRequired,
    IReadOnlyList<string?>? Labels,
    Guid? ChecklistTemplateId = null);

/// <summary>PATCH /tasks/{id}: the full editable field set plus the version that was loaded. Null type, priority or flag keeps the current value.</summary>
public sealed record UpdateTaskCommand(
    string? Title,
    string? Description,
    TaskType? Type,
    TaskPriority? Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    int? AllocatedMinutes,
    bool? AlgorithmRequired,
    IReadOnlyList<string?>? Labels,
    uint Version);

/// <summary>T2. <see cref="DueDate"/> may be supplied here when the task does not have one yet (WQ1).</summary>
public sealed record AssignTaskCommand(Guid AssigneeId, DateOnly? DueDate);

public sealed record CancelTaskCommand(string? Reason);

/// <summary>One entry in a task's timeline: a state change or an assignment.</summary>
public sealed record TaskHistoryEntryDto(
    DateTimeOffset OccurredAt,
    string Kind,
    UserRef? Actor,
    int? Revision,
    string? Action,
    TaskItemStatus? FromStatus,
    TaskItemStatus? ToStatus,
    UserRef? FromUser,
    UserRef? ToUser,
    string? Comment);

public sealed record TaskCommentDto(
    Guid Id,
    UserRef? Author,
    string? Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    bool IsDeleted,
    bool CanEdit,
    bool CanDelete);

/// <summary><paramref name="AttachmentIds"/> (new comments only): the caller's draft files uploaded with owner type Comment.</summary>
public sealed record SaveCommentCommand(string? Body, IReadOnlyList<Guid>? AttachmentIds = null);
