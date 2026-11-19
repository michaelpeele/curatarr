using Curatarr.Api;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Curatarr.Tests;

public class LoggingLevelSwitchTests
{
    [Theory]
    [InlineData("Trace", LogEventLevel.Verbose)]
    [InlineData("Debug", LogEventLevel.Debug)]
    [InlineData("Information", LogEventLevel.Information)]
    [InlineData("Warning", LogEventLevel.Warning)]
    [InlineData("Error", LogEventLevel.Error)]
    [InlineData("Critical", LogEventLevel.Fatal)]
    [InlineData("Bogus", LogEventLevel.Information)] // safe fallback
    public void ToSerilogLevel_maps_every_config_level(string name, LogEventLevel expected)
    {
        Assert.Equal(expected, LoggingConfig.ToSerilogLevel(name));
    }

    [Fact]
    public void Minimum_level_changes_when_the_switch_is_set()
    {
        var levelSwitch = new LoggingLevelSwitch(LoggingConfig.ToSerilogLevel("Information"));
        Assert.Equal(LogEventLevel.Information, levelSwitch.MinimumLevel);

        levelSwitch.MinimumLevel = LoggingConfig.ToSerilogLevel("Debug");
        Assert.Equal(LogEventLevel.Debug, levelSwitch.MinimumLevel);

        levelSwitch.MinimumLevel = LoggingConfig.ToSerilogLevel("Critical");
        Assert.Equal(LogEventLevel.Fatal, levelSwitch.MinimumLevel);
    }
}
