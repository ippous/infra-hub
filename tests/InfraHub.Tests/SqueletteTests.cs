using InfraHub.Core;

namespace InfraHub.Tests;

public class SqueletteTests
{
    [Fact]
    public void Core_est_reference()
    {
        Assert.Equal("InfraHub.Core", CoreAssembly.Name);
    }
}
