using TaskManagement.Domain.Common;

namespace TaskManagement.Application.Common;

internal static class CommonErrors
{
    public static Error NotFound(string entity) =>
        Error.NotFound($"{entity.ToLowerInvariant()}.not_found", $"The {entity.ToLowerInvariant()} was not found.");

    public static Error ConcurrencyConflict() =>
        Error.Conflict("concurrency_conflict", "This record was changed by someone else. Reload it and try again.");
}
