using System.Reflection;

namespace TaskManagement.ArchitectureTests;

/// <summary>Enforces the dependency direction in docs/architecture.md §3.</summary>
public sealed class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(TaskManagement.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(TaskManagement.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(TaskManagement.Infrastructure.DependencyInjection).Assembly;

    public static TheoryData<string, string> ForbiddenReferences => new()
    {
        { "TaskManagement.Domain", "TaskManagement.Application" },
        { "TaskManagement.Domain", "TaskManagement.Infrastructure" },
        { "TaskManagement.Domain", "TaskManagement.Api" },
        { "TaskManagement.Domain", "Microsoft.EntityFrameworkCore" },
        { "TaskManagement.Domain", "Microsoft.AspNetCore" },
        { "TaskManagement.Domain", "Microsoft.Extensions" },
        { "TaskManagement.Application", "TaskManagement.Infrastructure" },
        { "TaskManagement.Application", "TaskManagement.Api" },
        { "TaskManagement.Application", "Microsoft.EntityFrameworkCore" },
        { "TaskManagement.Application", "Microsoft.AspNetCore" },
        { "TaskManagement.Application", "Npgsql" },
        { "TaskManagement.Infrastructure", "TaskManagement.Api" },
        { "TaskManagement.Infrastructure", "Microsoft.AspNetCore" },
    };

    [Theory]
    [MemberData(nameof(ForbiddenReferences))]
    public void Layer_does_not_reference_forbidden_assembly(string layer, string forbiddenPrefix)
    {
        var assembly = layer switch
        {
            "TaskManagement.Domain" => Domain,
            "TaskManagement.Application" => Application,
            "TaskManagement.Infrastructure" => Infrastructure,
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };

        var violations = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.Equals(forbiddenPrefix, StringComparison.Ordinal)
                || name.StartsWith(forbiddenPrefix + ".", StringComparison.Ordinal))
            .ToList();

        Assert.True(violations.Count == 0, $"{layer} must not reference {forbiddenPrefix}, but references: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Domain_references_only_the_base_class_library()
    {
        var allowed = new[] { "System.Runtime", "System.Collections", "System.Linq", "netstandard" };

        var unexpected = Domain.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !allowed.Contains(name, StringComparer.Ordinal))
            .ToList();

        Assert.True(unexpected.Count == 0, $"Domain has unexpected references: {string.Join(", ", unexpected)}");
    }
}
