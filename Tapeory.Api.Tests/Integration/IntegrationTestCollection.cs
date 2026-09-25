namespace Tapeory.Api.Tests.Integration;

/// <summary>
/// Shares a single MySQL container across every integration test class so the container
/// only starts (and migrates) once per test run instead of once per test class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<TapeoryWebApplicationFactory>
{
    public const string Name = "Integration";
}
