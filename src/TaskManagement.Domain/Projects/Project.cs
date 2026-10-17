using TaskManagement.Domain.Common;

namespace TaskManagement.Domain.Projects;

/// <summary>
/// A project groups tasks and the members who work on them (FR-PROJ-01..04). The key (e.g. <c>TMA</c>) prefixes
/// every task key, so it cannot change after creation.
/// </summary>
public sealed class Project
{
    public const int KeyMinLength = 2;
    public const int KeyMaxLength = 10;
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 50_000;

    private Project()
    {
        Key = string.Empty;
        Name = string.Empty;
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Key { get; private set; }

    public string Name { get; private set; }

    /// <summary>Markdown; sanitized when rendered.</summary>
    public string Description { get; private set; }

    public ProjectStatus Status { get; private set; }

    public DateOnly? StartDate { get; private set; }

    public DateOnly? TargetEndDate { get; private set; }

    /// <summary>Default for a new task's "algorithm required" flag (FR-TASK-04).</summary>
    public bool AlgorithmRequiredDefault { get; private set; }

    /// <summary>Q2 option: the TL's approval closes the task directly.</summary>
    public bool CloseOnApproval { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; private set; }

    /// <summary>Completed and Archived projects, and their tasks, cannot be changed (FR-PROJ-04).</summary>
    public bool IsReadOnly => Status is ProjectStatus.Completed or ProjectStatus.Archived;

    /// <summary>Tasks can only be created in an Active project (task-workflow.md T1).</summary>
    public bool AcceptsNewTasks => Status == ProjectStatus.Active;

    /// <summary>2–10 upper-case letters or digits, starting with a letter (e.g. <c>TMA</c>, <c>WEB2</c>).</summary>
    public static bool IsValidKey(string? key)
    {
        if (key is null || key.Length is < KeyMinLength or > KeyMaxLength || key[0] is < 'A' or > 'Z')
        {
            return false;
        }

        foreach (var c in key)
        {
            if (c is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9')))
            {
                return false;
            }
        }

        return true;
    }

    public static Project Create(
        string key,
        string name,
        string? description,
        DateOnly? startDate,
        DateOnly? targetEndDate,
        bool algorithmRequiredDefault,
        bool closeOnApproval,
        Guid? createdBy,
        DateTimeOffset now)
    {
        if (!IsValidKey(key))
        {
            throw new ArgumentException("The project key must be 2–10 upper-case letters or digits, starting with a letter.", nameof(key));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Project
        {
            Id = Guid.CreateVersion7(now),
            Key = key,
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Status = ProjectStatus.Active,
            StartDate = startDate,
            TargetEndDate = targetEndDate,
            AlgorithmRequiredDefault = algorithmRequiredDefault,
            CloseOnApproval = closeOnApproval,
            CreatedAt = now,
            CreatedBy = createdBy,
            UpdatedAt = now,
            UpdatedBy = createdBy,
        };
    }

    public Result UpdateDetails(
        string name,
        string? description,
        DateOnly? startDate,
        DateOnly? targetEndDate,
        bool algorithmRequiredDefault,
        bool closeOnApproval,
        Guid by,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (IsReadOnly)
        {
            return ReadOnlyError();
        }

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        StartDate = startDate;
        TargetEndDate = targetEndDate;
        AlgorithmRequiredDefault = algorithmRequiredDefault;
        CloseOnApproval = closeOnApproval;
        Touch(by, now);
        return Result.Success();
    }

    /// <summary>Moves the project to <paramref name="target"/> if the change is allowed from its current status.</summary>
    public Result ChangeStatus(ProjectStatus target, Guid by, DateTimeOffset now)
    {
        var allowed = (Status, target) switch
        {
            (ProjectStatus.Active, ProjectStatus.OnHold) => true,
            (ProjectStatus.Active or ProjectStatus.OnHold, ProjectStatus.Completed) => true,
            (not ProjectStatus.Archived, ProjectStatus.Archived) => true,
            (not ProjectStatus.Active, ProjectStatus.Active) => true,
            _ => false,
        };
        if (!allowed)
        {
            return Error.Conflict(
                "project.invalid_status_change",
                $"A project that is {Describe(Status)} cannot be changed to {Describe(target)}.");
        }

        Status = target;
        Touch(by, now);
        return Result.Success();
    }

    public static Error ReadOnlyError() =>
        Error.Conflict("project.read_only", "This project is completed or archived, so it and its tasks are read-only. Reactivate it first.");

    public static string Describe(ProjectStatus status) => status switch
    {
        ProjectStatus.OnHold => "on hold",
        _ => status.ToString().ToLowerInvariant(),
    };

    private void Touch(Guid by, DateTimeOffset now)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
