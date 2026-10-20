using NSubstitute;
using Quadra.Infrastructure.Storage;

namespace Quadra.IntegrationTests;

internal static class TestPhotoStorage
{
    /// <summary>A substitute photo storage whose uploads fit the size limit unless a test says otherwise.</summary>
    public static IProfilePhotoStorage Create()
    {
        var storage = Substitute.For<IProfilePhotoStorage>();
        storage.IsWithinLimitsAsync(default!, default).ReturnsForAnyArgs(true);
        return storage;
    }
}
