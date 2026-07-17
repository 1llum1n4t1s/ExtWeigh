using ExtWeigh.Core.Measurement;

namespace ExtWeigh.Tests;

[TestClass]
public sealed class KeyboardShortcutTests
{
    [TestMethod]
    public void Parse_CtrlShiftY()
    {
        var s = KeyboardShortcut.Parse("Ctrl+Shift+Y");
        Assert.AreEqual(2 | 8, s.Modifiers);
        Assert.AreEqual("Y", s.Key);
        Assert.AreEqual("KeyY", s.Code);
        Assert.AreEqual('Y', s.VirtualKeyCode);
    }

    [TestMethod]
    public void Parse_AltのみとF5()
    {
        var s = KeyboardShortcut.Parse("Alt+F5");
        Assert.AreEqual(1, s.Modifiers);
        Assert.AreEqual("F5", s.Key);
        Assert.AreEqual(0x74, s.VirtualKeyCode);
    }

    [TestMethod]
    public void Parse_数字キー()
    {
        var s = KeyboardShortcut.Parse("Ctrl+1");
        Assert.AreEqual("Digit1", s.Code);
        Assert.AreEqual('1', s.VirtualKeyCode);
    }

    [TestMethod]
    public void Parse_小文字も許容する()
    {
        var s = KeyboardShortcut.Parse("ctrl+shift+y");
        Assert.AreEqual(2 | 8, s.Modifiers);
        Assert.AreEqual("Y", s.Key);
    }

    [TestMethod]
    public void Parse_メインキーなしは例外()
    {
        Assert.ThrowsExactly<FormatException>(() => KeyboardShortcut.Parse("Ctrl+Shift"));
    }

    [TestMethod]
    public void Parse_空文字は例外()
    {
        Assert.ThrowsExactly<FormatException>(() => KeyboardShortcut.Parse(""));
    }

    [TestMethod]
    public void Parse_未対応キーは例外()
    {
        Assert.ThrowsExactly<FormatException>(() => KeyboardShortcut.Parse("Ctrl+MediaPlayPause"));
    }
}
