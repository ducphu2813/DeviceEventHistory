using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.UnitTests;

public sealed class ProjectionDefinitionResolverTests
{
    [Fact]
    public async Task Concurrent_create_resolves_one_definition()
    {
        var store = new InMemoryDefinitionStore();
        var resolver = new ProjectionDefinitionResolver(store);
        var request = CreateRequest();

        var definitions = await Task.WhenAll(
            resolver.ResolveAsync(request),
            resolver.ResolveAsync(request));

        Assert.Equal(1, store.CreateCount);
        Assert.All(definitions, definition =>
            Assert.Equal(request.Identity, definition.Identity));
    }

    [Fact]
    public async Task Resume_uses_stored_coverage_when_config_omits_it()
    {
        var stored = CreateDefinition(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var resolver = new ProjectionDefinitionResolver(new InMemoryDefinitionStore(stored));

        var result = await resolver.ResolveAsync(
            CreateRequest() with
            {
                ResumeFromStoredDefinition = true,
                CoverageStartAtUtc = null
            });

        Assert.Equal(stored.CoverageStartAtUtc, result.CoverageStartAtUtc);
        Assert.Equal(stored.MappingVersion, result.MappingVersion);
        Assert.False(result.IsNew);
    }

    [Fact]
    public async Task Immutable_contract_mismatch_fails_before_processing()
    {
        var resolver = new ProjectionDefinitionResolver(
            new InMemoryDefinitionStore(CreateDefinition(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(
            CreateRequest() with { MappingVersion = "v2" }));

        Assert.StartsWith(
            StatisticsContractConstants.MessageCodePrefix + "RECOVERY-DEFINITION-CONFLICT",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resume_missing_definition_is_rejected()
    {
        var resolver = new ProjectionDefinitionResolver(new InMemoryDefinitionStore());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(
            CreateRequest() with
            {
                ResumeFromStoredDefinition = true,
                CoverageStartAtUtc = null
            }));

        Assert.StartsWith(
            StatisticsContractConstants.MessageCodePrefix + "RECOVERY-DEFINITION-MISSING",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static ProjectionDefinitionResolutionRequest CreateRequest() =>
        new(
            ProjectionIdentity.Default(),
            "v1",
            "v1",
            1,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "Asia/Ho_Chi_Minh",
            false,
            false);

    private static ResolvedProjectionDefinition CreateDefinition(DateTimeOffset coverageStartAtUtc) =>
        new(
            ProjectionIdentity.Default(),
            "v1",
            "v1",
            1,
            coverageStartAtUtc,
            "Asia/Ho_Chi_Minh",
            ProjectionLifecycleStatuses.Active,
            false);

    private sealed class InMemoryDefinitionStore(
        ResolvedProjectionDefinition? initial = null) : IProjectionDefinitionStore
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        private ResolvedProjectionDefinition? definition = initial;

        public int CreateCount { get; private set; }

        public async Task<ResolvedProjectionDefinition?> ResolveOrCreateAsync(
            ProjectionDefinitionResolutionRequest request,
            string lifecycleStatus,
            CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (definition is not null || request.ResumeFromStoredDefinition)
                {
                    return definition;
                }

                CreateCount++;
                definition = new ResolvedProjectionDefinition(
                    request.Identity,
                    request.MappingVersion,
                    request.OwnershipVersion,
                    request.MetricSetVersion,
                    request.CoverageStartAtUtc!.Value,
                    request.TimeZoneId,
                    lifecycleStatus,
                    true);
                return definition;
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
