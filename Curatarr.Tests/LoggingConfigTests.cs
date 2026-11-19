using Curatarr.Api;
using Xunit;

namespace Curatarr.Tests;

public class LoggingConfigTests
{
    [Theory]
    [InlineData("Trace", "Trace")]
    [InlineData("Debug", "Debug")]
    [InlineData("Information", "Information")]
    [InlineData("Warning", "Warning")]
    [InlineData("Error", "Error")]
    [InlineData("Critical", "Critical")]
    [InlineData("debug", "Debug")] // case is canonicalised
    public void IsValidLevel_accepts_all_levels(string input, string expected)
    {
        Assert.True(LoggingConfig.IsValidLevel(input, out var canonical, out var error), error);
        Assert.Equal(expected, canonical);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Verbose")]
    [InlineData("Critical!")]
    public void IsValidLevel_rejects_missing_or_unknown_levels(string? input)
    {
        Assert.False(LoggingConfig.IsValidLevel(input, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(14)]
    [InlineData(21)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    public void IsValid_accepts_every_supported_retention(int days)
    {
        var config = new LoggingConfig { Level = "Information", RetentionDays = days, FileSizeLimitMB = 10 };
        Assert.True(config.IsValid(out var error), error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(91)]
    public void IsValid_rejects_unsupported_retention(int days)
    {
        var config = new LoggingConfig { Level = "Information", RetentionDays = days, FileSizeLimitMB = 10 };
        Assert.False(config.IsValid(out var error));
        Assert.Contains("retention", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void IsValid_rejects_out_of_range_file_size(int size)
    {
        var config = new LoggingConfig { Level = "Information", RetentionDays = 7, FileSizeLimitMB = size };
        Assert.False(config.IsValid(out var error));
        Assert.Contains("size", error);
    }

    [Fact]
    public void IsValid_rejects_invalid_level()
    {
        var config = new LoggingConfig { Level = "Bogus", RetentionDays = 7, FileSizeLimitMB = 10 };
        Assert.False(config.IsValid(out var error));
        Assert.Contains("level", error);
    }

    [Fact]
    public void Defaults_are_valid()
    {
        Assert.True(new LoggingConfig().IsValid(out var error), error);
    }
}
