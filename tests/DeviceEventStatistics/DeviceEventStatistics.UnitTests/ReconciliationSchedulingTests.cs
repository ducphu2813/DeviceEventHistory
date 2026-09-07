using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Projection;

namespace DeviceEventStatistics.UnitTests;

public sealed class ReconciliationSchedulingTests
{
    [Fact]
    public void Global_scope_uses_observed_pairs_without_cross_joining()
    {
        var candidates = new[]
        {
            new ProjectionDeviceKey(2, 18),
            new ProjectionDeviceKey(2, 40),
            new ProjectionDeviceKey(3, 18)
        };

        var selected = ProjectionScopeSelector.Select(candidates, [], []);

        Assert.Equal(
            [new ProjectionDeviceKey(2, 18), new ProjectionDeviceKey(2, 40), new ProjectionDeviceKey(3, 18)],
            selected);
    }

    [Fact]
    public void Explicit_scope_filters_existing_pairs_instead_of_creating_new_ones()
    {
        var candidates = new[]
        {
            new ProjectionDeviceKey(2, 18),
            new ProjectionDeviceKey(3, 40)
        };

        var selected = ProjectionScopeSelector.Select(candidates, [2, 3], [18, 40]);

        Assert.Equal(
            [new ProjectionDeviceKey(2, 18), new ProjectionDeviceKey(3, 40)],
            selected);
    }

    [Fact]
    public void A_single_filter_dimension_is_supported()
    {
        var candidates = new[]
        {
            new ProjectionDeviceKey(2, 18),
            new ProjectionDeviceKey(2, 40),
            new ProjectionDeviceKey(3, 18)
        };

        var selected = ProjectionScopeSelector.Select(candidates, [2], []);

        Assert.Equal(
            [new ProjectionDeviceKey(2, 18), new ProjectionDeviceKey(2, 40)],
            selected);
    }
}
