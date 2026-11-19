using Curatarr.Api;
using Xunit;

namespace Curatarr.Tests;

public class ActivityRedactionTests
{
    [Theory]
    [InlineData("password=hunter2", "hunter2")]
    [InlineData("token: abc123", "abc123")]
    [InlineData("secret=topsecret", "topsecret")]
    [InlineData("api_key=KEY123", "KEY123")]
    [InlineData("client-secret: CLIENT1", "CLIENT1")]
    public void Sanitize_redacts_secret_assignments(string input, string secret)
    {
        var result = ActivityRedaction.Sanitize(input);
        Assert.DoesNotContain(secret, result);
        Assert.Contains("[redacted]", result);
    }

    [Theory]
    [InlineData("https://curatarr.local/reset-password?email=a@b.com&token=ABC123")]
    [InlineData("https://curatarr.local/accept-invite?userId=1&token=ABC123")]
    [InlineData("http://curatarr.local/invite/ABC123")]
    public void Sanitize_redacts_sensitive_links(string input)
    {
        var result = ActivityRedaction.Sanitize(input);
        Assert.Contains("[link redacted]", result);
        Assert.DoesNotContain("ABC123", result);
        Assert.DoesNotContain("http", result);
    }

    [Theory]
    [InlineData("Client sent Bearer abc", "abc")]
    [InlineData("Request used Bearer abc.def-123", "abc.def-123")]
    public void Sanitize_redacts_bearer_tokens(string input, string token)
    {
        var result = ActivityRedaction.Sanitize(input);
        Assert.Contains("Bearer [redacted]", result);
        Assert.DoesNotContain(token, result);
    }

    [Fact]
    public void Sanitize_truncates_long_details()
    {
        var result = ActivityRedaction.Sanitize(new string('x', 500));
        Assert.Equal(ActivityRedaction.MaxDetailLength + 1, result.Length); // 400 chars + ellipsis
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void Sanitize_passes_through_ordinary_details()
    {
        Assert.Equal("Scan finished: 3 scanned, 1 created",
            ActivityRedaction.Sanitize("Scan finished: 3 scanned, 1 created"));
        Assert.Equal("", ActivityRedaction.Sanitize(null));
        Assert.Equal("", ActivityRedaction.Sanitize("   "));
    }
}
