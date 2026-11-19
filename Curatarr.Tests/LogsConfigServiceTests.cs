using Curatarr.Api;
using Xunit;

namespace Curatarr.Tests;

public class LogsConfigServiceTests
{
    [Fact]
    public void Updates_round_trip_through_logs_config_json()
    {
        var root = NewRoot();
        try
        {
            var service = new LogsConfigService(root);
            Assert.Null(service.LoadError);
            Assert.Equal("Information", service.Current.Level);
            Assert.Equal(7, service.Current.RetentionDays);
            Assert.False(File.Exists(Path.Combine(root, "logs-config.json")));

            Assert.True(service.TryUpdateLevel("Debug", out var error), error);
            Assert.True(service.TryUpdateConfig(30, 25, false, out error), error);

            var current = service.Current;
            Assert.Equal("Debug", current.Level);
            Assert.Equal(30, current.RetentionDays);
            Assert.Equal(25, current.FileSizeLimitMB);
            Assert.False(current.ArchiveEnabled);
            Assert.True(File.Exists(Path.Combine(root, "logs-config.json")));

            var reloaded = new LogsConfigService(root);
            Assert.Null(reloaded.LoadError);
            Assert.Equal(current.Level, reloaded.Current.Level);
            Assert.Equal(current.RetentionDays, reloaded.Current.RetentionDays);
            Assert.Equal(current.FileSizeLimitMB, reloaded.Current.FileSizeLimitMB);
            Assert.Equal(current.ArchiveEnabled, reloaded.Current.ArchiveEnabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Malformed_file_falls_back_to_defaults_and_sets_load_error()
    {
        var root = NewRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "logs-config.json"), "{ this is not json");
            var service = new LogsConfigService(root);

            Assert.NotNull(service.LoadError);
            Assert.Contains("logs-config.json", service.LoadError);
            Assert.Equal("Information", service.Current.Level);
            Assert.Equal(7, service.Current.RetentionDays);
            Assert.Equal(10, service.Current.FileSizeLimitMB);
            Assert.True(service.Current.ArchiveEnabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Semantically_invalid_file_also_falls_back_to_defaults()
    {
        var root = NewRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "logs-config.json"),
                "{ \"Level\": \"Bogus\", \"RetentionDays\": 7, \"FileSizeLimitMB\": 10, \"ArchiveEnabled\": true }");
            var service = new LogsConfigService(root);

            Assert.NotNull(service.LoadError);
            Assert.Equal("Information", service.Current.Level);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Invalid_updates_are_rejected_without_changing_current_config()
    {
        var root = NewRoot();
        try
        {
            var service = new LogsConfigService(root);
            Assert.False(service.TryUpdateLevel("Verbose", out var error));
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.False(service.TryUpdateConfig(5, 10, true, out error));
            Assert.Equal("Information", service.Current.Level);
            Assert.Equal(7, service.Current.RetentionDays);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "curatarr-logs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
