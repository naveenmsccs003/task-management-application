using TaskManagement.Domain.Common;

namespace TaskManagement.Application.Common;

internal static class CommonErrors
{
    public static Error NotFound(string entity) =>
        Error.NotFound($"{entity.ToLowerInvariant()}.not_found", $"The {entity.ToLowerInvariant()} was not found.");

    /// <summary>The caller can see the record but may not perform this action (docs/api-design.md §2, step 2).</summary>
    public static Error Forbidden(string action) =>
        Error.Forbidden("forbidden", $"You do not have permission to {action}.");

    public static Error ConcurrencyConflict() =>
        Error.Conflict("concurrency_conflict", "This record was changed by someone else. Reload it and try again.");
}
