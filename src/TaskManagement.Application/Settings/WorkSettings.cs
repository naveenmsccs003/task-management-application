using TaskManagement.Application.Common;

namespace TaskManagement.Application.Settings;

/// <summary>Work-session settings editable by Admins (FR-ROLE-08, FR-WORK-03, FR-TIME-06, FR-TIME-09).</summary>
public sealed record WorkSettings
{
    public const string Key = "work";

    /// <summary>Sessions running longer are closed by the background job (default 10 hours).</summary>
    public int MaxSessionHours { get; init; } = 10;

    /// <summary>Pause reasons are optional by default.</summary>
    public bool PauseReasonRequired { get; init; }

    /// <summary>Share of allocated hours at which the assignee is warned (notifications phase).</summary>
    public int AllocationWarningPercent { get; init; } = 80;

    internal FieldErrors Validate()
    {
        var errors = new FieldErrors();
        if (MaxSessionHours is < 1 or > 24)
        {
            errors.Add("maxSessionHours", "Must be between 1 and 24.");
        }

        if (AllocationWarningPercent is < 50 or > 99)
        {
            errors.Add("allocationWarningPercent", "Must be between 50 and 99.");
        }

        return errors;
    }
}
