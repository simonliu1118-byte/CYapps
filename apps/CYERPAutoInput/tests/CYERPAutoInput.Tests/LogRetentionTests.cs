using Xunit;

namespace CYERPAutoInput.Tests;

public class LogRetentionTests
{
    [Theory]
    [InlineData("2026-11-14 11:17:07.927 [ERROR] import: InvalidOperationException: x", true)]
    [InlineData("2026-11-26 13:02:26.230 [WARN] shopee-batch: stopped: 等不到頁面（30 秒）。", true)]
    [InlineData("2026-11-12 11:34:39.748 [WARN] shopee-batch: timeout waiting for 酷澎 found=False", true)]
    [InlineData("2026-11-26 14:09:39.716 [INFO] automation: Esc pressed: stopping", true)]
    [InlineData("2026-11-15 11:28:49.777 [WARN] document: warning recorded code=BATCH_STOCK_INSUFFICIENT document=2", false)]
    [InlineData("2026-11-14 11:16:17.412 [WARN] print-watch: printer=X 工作卡在列印佇列沒有印出", false)]
    [InlineData("2026-11-14 11:16:20.996 [WARN] vision: fast detail row-line detection failed; falling back", false)]
    [InlineData("2026-11-26 09:58:21.385 [INFO] shopee-batch: ended outcome=Done done=7", false)]
    public void TroubleLines(string line, bool trouble) => Assert.Equal(trouble, LogRetention.IsTrouble(line));

    [Fact]
    public void OldQuietLogsGoTroubledAndRecentStay()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cy-log-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var today = new DateTime(2026, 11, 26);
            File.WriteAllText(Path.Combine(dir, "CYERPAutoInput_20261110.log"), "x [INFO] app: starting\nx [WARN] document: warning recorded code=BATCH_STOCK_INSUFFICIENT\n");
            File.WriteAllText(Path.Combine(dir, "CYERPAutoInput_20261111.log"), "x [ERROR] import: boom\n");
            File.WriteAllText(Path.Combine(dir, "CYERPAutoInput_20261125.log"), "x [INFO] app: starting\n");
            File.WriteAllText(Path.Combine(dir, "other.log"), "x\n");
            var removed = LogRetention.Prune(dir, today, _ => { });
            Assert.Equal(1, removed);
            Assert.False(File.Exists(Path.Combine(dir, "CYERPAutoInput_20261110.log")));
            Assert.True(File.Exists(Path.Combine(dir, "CYERPAutoInput_20261111.log")));
            Assert.True(File.Exists(Path.Combine(dir, "CYERPAutoInput_20261125.log")));
            Assert.True(File.Exists(Path.Combine(dir, "other.log")));
        }
        finally { Directory.Delete(dir, true); }
    }
}
