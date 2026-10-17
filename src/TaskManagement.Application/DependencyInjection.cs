using Microsoft.Extensions.DependencyInjection;

namespace TaskManagement.Application;

public static class DependencyInjection
{
    /// <summary>Registers use-case handlers. Modules add their registrations here as they are built.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
