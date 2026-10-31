using ExtWeigh.Core.Logging;

namespace ExtWeigh.Tests;

[TestClass]
public sealed class LegacyLogMigrationTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ExtWeigh-log-migration-tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void 旧ログだけを移し同名と利用者ファイルを保持する()
    {
        var source = Path.Combine(_root, "old");
        var destination = Path.Combine(_root, "new");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "ExtWeigh_2026-10-30.log"), "old");
        File.WriteAllText(Path.Combine(source, "keep.txt"), "user");
        File.WriteAllText(Path.Combine(destination, "ExtWeigh_2026-10-30.log"), "new");

        LegacyLogMigration.Migrate(source, destination);

        Assert.AreEqual("new", File.ReadAllText(Path.Combine(destination, "ExtWeigh_2026-10-30.log")));
        Assert.AreEqual("old", File.ReadAllText(Directory.GetFiles(destination, "ExtWeigh_*_legacy_*.log").Single()));
        Assert.IsTrue(File.Exists(Path.Combine(source, "keep.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(source, "ExtWeigh_2026-10-30.log")));
    }

    [TestMethod]
    public void 移行した旧ログにも保持期限を適用する()
    {
        var source = Path.Combine(_root, "old");
        var destination = Path.Combine(_root, "new");
        Directory.CreateDirectory(source);
        var oldDate = DateTime.Now.Date.AddDays(-8).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(Path.Combine(source, $"ExtWeigh_{oldDate}.log"), "old");

        LegacyLogMigration.Migrate(source, destination);
        Assert.AreEqual(1, Directory.GetFiles(destination).Length);
        LoggerService.CleanupOldLogFiles(destination);

        Assert.AreEqual(0, Directory.GetFiles(destination).Length);
    }

    [TestMethod]
    public void 使用中の旧ログは残し次回移行する()
    {
        var source = Path.Combine(_root, "old");
        var destination = Path.Combine(_root, "new");
        Directory.CreateDirectory(source);
        var file = Path.Combine(source, "ExtWeigh_2026-10-30.log");
        File.WriteAllText(file, "old");
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            LegacyLogMigration.Migrate(source, destination);
            Assert.IsTrue(File.Exists(file));
            Assert.AreEqual(0, Directory.GetFiles(destination).Length);
        }

        LegacyLogMigration.Migrate(source, destination);
        Assert.IsFalse(Directory.Exists(source));
        Assert.AreEqual("old", File.ReadAllText(Directory.GetFiles(destination).Single()));
    }

    [TestMethod]
    public void リンク先のログは移動しない()
    {
        var source = Path.Combine(_root, "old");
        var destination = Path.Combine(_root, "new");
        Directory.CreateDirectory(source);
        var external = Path.Combine(_root, "external.log");
        File.WriteAllText(external, "external");
        try
        {
            File.CreateSymbolicLink(Path.Combine(source, "ExtWeigh_2026-10-30.log"), external);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException ||
                                   ex is IOException && (ex.HResult & 0xffff) == 1314)
        {
            Assert.Inconclusive("シンボリックリンクの作成権限がありません。");
        }

        LegacyLogMigration.Migrate(source, destination);

        Assert.AreEqual("external", File.ReadAllText(external));
        Assert.AreEqual(0, Directory.GetFiles(destination).Length);
    }

    [TestMethod]
    public void 並行移行しても旧ログは一度だけ残る()
    {
        var source = Path.Combine(_root, "old");
        var destination = Path.Combine(_root, "new");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "ExtWeigh_2026-10-30.log"), "old");

        Parallel.For(0, 8, _ => LegacyLogMigration.Migrate(source, destination));

        Assert.AreEqual("old", File.ReadAllText(Directory.GetFiles(destination).Single()));
        Assert.IsFalse(Directory.Exists(source));
    }
}
