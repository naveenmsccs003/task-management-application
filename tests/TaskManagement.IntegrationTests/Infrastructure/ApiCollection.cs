namespace TaskManagement.IntegrationTests.Infrastructure;

/// <summary>Tests in this collection share one API host and database, created once.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
