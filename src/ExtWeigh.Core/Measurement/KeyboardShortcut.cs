namespace ExtWeigh.Core.Measurement;

/// <summary>
/// "Ctrl+Shift+Y" 形式のショートカット文字列を CDP Input.dispatchKeyEvent 用の
/// パラメータへ変換する。
/// </summary>
public sealed class KeyboardShortcut
{
    /// <summary>CDP modifiers ビットマスク (Alt=1, Ctrl=2, Meta=4, Shift=8)</summary>
    public int Modifiers { get; }

    /// <summary>DOM key 値（例: "Y", "F5"）</summary>
    public string Key { get; }

    /// <summary>DOM code 値（例: "KeyY", "F5"）</summary>
    public string Code { get; }

    /// <summary>Windows 仮想キーコード</summary>
    public int VirtualKeyCode { get; }

    private KeyboardShortcut(int modifiers, string key, string code, int virtualKeyCode)
    {
        Modifiers = modifiers;
        Key = key;
        Code = code;
        VirtualKeyCode = virtualKeyCode;
    }

    /// <summary>ショートカット文字列を解析する。未対応キーは <see cref="FormatException"/>。</summary>
    public static KeyboardShortcut Parse(string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) throw new FormatException("ショートカットが空です");

        var modifiers = 0;
        string? mainKey = null;

        foreach (var raw in shortcut.Split('+'))
        {
            var token = raw.Trim();
            switch (token.ToLowerInvariant())
            {
                case "alt": modifiers |= 1; break;
                case "ctrl" or "control": modifiers |= 2; break;
                case "command" or "meta" or "macctrl": modifiers |= 4; break;
                case "shift": modifiers |= 8; break;
                case "": throw new FormatException($"ショートカットの書式が不正です: {shortcut}");
                default:
                    if (mainKey is not null) throw new FormatException($"メインキーが複数あります: {shortcut}");
                    mainKey = token;
                    break;
            }
        }

        if (mainKey is null) throw new FormatException($"メインキーがありません: {shortcut}");

        // A-Z
        if (mainKey.Length == 1 && char.IsAsciiLetter(mainKey[0]))
        {
            var upper = char.ToUpperInvariant(mainKey[0]);
            return new KeyboardShortcut(modifiers, upper.ToString(), $"Key{upper}", upper);
        }

        // 0-9
        if (mainKey.Length == 1 && char.IsAsciiDigit(mainKey[0]))
        {
            return new KeyboardShortcut(modifiers, mainKey, $"Digit{mainKey}", mainKey[0]);
        }

        // F1-F12
        if ((mainKey.StartsWith('F') || mainKey.StartsWith('f')) &&
            int.TryParse(mainKey[1..], out var fn) && fn is >= 1 and <= 12)
        {
            return new KeyboardShortcut(modifiers, $"F{fn}", $"F{fn}", 0x70 + fn - 1);
        }

        // その他の特殊キー
        return mainKey.ToLowerInvariant() switch
        {
            "space" => new KeyboardShortcut(modifiers, " ", "Space", 0x20),
            "comma" => new KeyboardShortcut(modifiers, ",", "Comma", 0xBC),
            "period" => new KeyboardShortcut(modifiers, ".", "Period", 0xBE),
            "home" => new KeyboardShortcut(modifiers, "Home", "Home", 0x24),
            "end" => new KeyboardShortcut(modifiers, "End", "End", 0x23),
            "pageup" => new KeyboardShortcut(modifiers, "PageUp", "PageUp", 0x21),
            "pagedown" => new KeyboardShortcut(modifiers, "PageDown", "PageDown", 0x22),
            "insert" => new KeyboardShortcut(modifiers, "Insert", "Insert", 0x2D),
            "delete" => new KeyboardShortcut(modifiers, "Delete", "Delete", 0x2E),
            "up" => new KeyboardShortcut(modifiers, "ArrowUp", "ArrowUp", 0x26),
            "down" => new KeyboardShortcut(modifiers, "ArrowDown", "ArrowDown", 0x28),
            "left" => new KeyboardShortcut(modifiers, "ArrowLeft", "ArrowLeft", 0x25),
            "right" => new KeyboardShortcut(modifiers, "ArrowRight", "ArrowRight", 0x27),
            _ => throw new FormatException($"未対応のキーです: {mainKey}"),
        };
    }
}
