using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>
/// A test that changes the REAL machine for a moment (power plan, mouse...). Without CS2H_LIVE=1 it is reported as SKIPPED -
/// not silently passed, which would make "all green" mean less than it says.
/// </summary>
public sealed class RealMachineFactAttribute : FactAttribute
{
    public RealMachineFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CS2H_LIVE") != "1")
            Skip = "CS2H_LIVE=1 değil: bu test gerçek makineyi kısa süre değiştirir, isteğe bağlıdır.";
    }
}

[Collection("Sequential")]
public class OptInFactTests
{
    [Fact]
    public void A_real_machine_test_is_reported_as_skipped_unless_the_owner_opted_in()
    {
        var old = Environment.GetEnvironmentVariable("CS2H_LIVE");
        try
        {
            Environment.SetEnvironmentVariable("CS2H_LIVE", null);
            Assert.False(string.IsNullOrEmpty(new RealMachineFactAttribute().Skip));
            Environment.SetEnvironmentVariable("CS2H_LIVE", "0");
            Assert.False(string.IsNullOrEmpty(new RealMachineFactAttribute().Skip));
            Environment.SetEnvironmentVariable("CS2H_LIVE", "1");
            Assert.Null(new RealMachineFactAttribute().Skip);
        }
        finally { Environment.SetEnvironmentVariable("CS2H_LIVE", old); }
    }
}
