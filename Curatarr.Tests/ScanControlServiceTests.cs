using Curatarr.Api;
using Xunit;

namespace Curatarr.Tests;

public class ScanControlServiceTests
{
    [Fact]
    public async Task Eta_is_null_before_first_item_and_positive_after_progress_but_not_while_paused()
    {
        var scan = new ScanControlService();
        scan.BeginNewScan();

        var initial = scan.Snapshot();
        Assert.True(initial.Running);
        Assert.Equal(0, initial.Completed);
        Assert.Null(initial.EtaSeconds);

        // Ensure a non-zero elapsed time so the computed ETA is deterministic.
        await Task.Delay(10);
        scan.ReportProgress(2, 10);
        var progressing = scan.Snapshot();
        Assert.True(progressing.Running);
        Assert.Equal(2, progressing.Completed);
        Assert.Equal(10, progressing.Total);
        Assert.True(progressing.EtaSeconds > 0);

        scan.Pause();
        var paused = scan.Snapshot();
        Assert.True(paused.Paused);
        Assert.Null(paused.EtaSeconds);

        scan.Resume();
        scan.ReportProgress(3, 10);
        var resumed = scan.Snapshot();
        Assert.False(resumed.Paused);
        Assert.True(resumed.EtaSeconds > 0);
    }

    [Fact]
    public void Finish_and_stop_leave_running_false_with_no_eta()
    {
        var finishedScan = new ScanControlService();
        finishedScan.BeginNewScan();
        finishedScan.ReportProgress(5, 10);
        Assert.True(finishedScan.Snapshot().Running);

        finishedScan.Finish();
        var finished = finishedScan.Snapshot();
        Assert.False(finished.Running);
        Assert.False(finished.Paused);
        Assert.Equal(5, finished.Completed);
        Assert.Null(finished.EtaSeconds);

        var stoppedScan = new ScanControlService();
        stoppedScan.BeginNewScan();
        stoppedScan.ReportProgress(2, 10);
        stoppedScan.Stop();
        var stopped = stoppedScan.Snapshot();
        Assert.False(stopped.Running);
        Assert.False(stopped.Paused);
        Assert.True(stopped.Stopped);
        Assert.Null(stopped.EtaSeconds);
    }

    [Fact]
    public void Snapshot_before_any_scan_is_idle()
    {
        var snapshot = new ScanControlService().Snapshot();
        Assert.False(snapshot.Running);
        Assert.Null(snapshot.StartedAt);
        Assert.Equal(0, snapshot.Completed);
        Assert.Equal(0, snapshot.Total);
        Assert.Null(snapshot.EtaSeconds);
    }
}
