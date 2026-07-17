using ExtWeigh.Core.Chrome;

namespace ExtWeigh.Tests;

[TestClass]
public sealed class ChromeLauncherTests
{
    [TestMethod]
    public void BuildArgs_複数拡張をカンマ区切りで読み込む()
    {
        var options = new ChromeLaunchOptions
        {
            ChromePath = "chrome.exe",
            UserDataDir = @"C:\temp\profile",
            ExtensionPaths = [@"C:\ext A", @"C:\ext B"],
        };

        var args = ChromeLauncher.BuildArgs(options);

        CollectionAssert.Contains(args, @"--disable-extensions-except=C:\ext A,C:\ext B");
        CollectionAssert.Contains(args, @"--load-extension=C:\ext A,C:\ext B");
        CollectionAssert.DoesNotContain(args, "--disable-extensions");
    }

    [TestMethod]
    public void BuildArgs_拡張なしは全拡張を無効化する()
    {
        var options = new ChromeLaunchOptions
        {
            ChromePath = "chrome.exe",
            UserDataDir = @"C:\temp\profile",
        };

        var args = ChromeLauncher.BuildArgs(options);

        CollectionAssert.Contains(args, "--disable-extensions");
    }
}
