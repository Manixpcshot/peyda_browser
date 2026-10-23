using PeydaBrowser.App.ViewModels;
using PeydaBrowser.Core.Shortcuts;
using Windows.System;

namespace PeydaBrowser.App.Services;

/// <summary>
/// Bridges WinUI keyboard input (KeyboardAccelerators on the window root and
/// KeyDown events forwarded from the WebView2 control) to the data-driven
/// shortcut table in <see cref="DefaultShortcuts"/>.
/// </summary>
public static class KeyboardShortcutRouter
{
    private static readonly IReadOnlyDictionary<ShortcutGesture, ShortcutDefinition> Map =
        DefaultShortcuts.All.ToDictionary(d => d.Gesture);

    public static bool TryCreateGesture(VirtualKey key, bool ctrl, bool alt, bool shift, out ShortcutGesture gesture)
    {
        gesture = default;
        var keyName = KeyName(key);
        if (keyName is null) return false;
        gesture = new ShortcutGesture(ctrl, alt, shift, keyName);
        return Map.ContainsKey(gesture);
    }

    public static async Task<bool> TryExecuteAsync(ShortcutGesture gesture, MainViewModel shell)
    {
        if (!Map.TryGetValue(gesture, out var definition)) return false;
        await shell.ExecuteShortcutAsync(definition.Command, definition.Argument).ConfigureAwait(true);
        return true;
    }

    public static bool TryToVirtualKey(ShortcutGesture gesture, out VirtualKey key)
    {
        key = default;
        switch (gesture.Key)
        {
            case "Tab": key = VirtualKey.Tab; return true;
            case "Left": key = VirtualKey.Left; return true;
            case "Right": key = VirtualKey.Right; return true;
            case "Up": key = VirtualKey.Up; return true;
            case "Down": key = VirtualKey.Down; return true;
            case "Escape": key = VirtualKey.Escape; return true;
            case "Enter": key = VirtualKey.Enter; return true;
            case "Plus": key = (VirtualKey)0xBB; return true;   // VK_OEM_PLUS
            case "Minus": key = (VirtualKey)0xBD; return true;  // VK_OEM_MINUS
        }
        if (gesture.Key.Length >= 2 && gesture.Key[0] == 'F' && int.TryParse(gesture.Key[1..], out var f) && f is >= 1 and <= 12)
        {
            key = (VirtualKey)((int)VirtualKey.F1 + (f - 1));
            return true;
        }
        if (gesture.Key.Length == 1)
        {
            var c = gesture.Key[0];
            if (c is >= 'A' and <= 'Z') { key = (VirtualKey)c; return true; }
            if (c is >= '0' and <= '9') { key = (VirtualKey)c; return true; }
        }
        return false;
    }

    private static string? KeyName(VirtualKey key)
    {
        switch (key)
        {
            case VirtualKey.Tab: return "Tab";
            case VirtualKey.Left: return "Left";
            case VirtualKey.Right: return "Right";
            case VirtualKey.Up: return "Up";
            case VirtualKey.Down: return "Down";
            case VirtualKey.Escape: return "Escape";
            case VirtualKey.Enter: return "Enter";
            case (VirtualKey)0xBB: return "Plus";
            case (VirtualKey)0xBD: return "Minus";
            case (VirtualKey)0x6B: return "Plus"; // numpad +
            case (VirtualKey)0x6D: return "Minus"; // numpad -
        }
        var code = (int)key;
        if (code >= (int)VirtualKey.F1 && code <= (int)VirtualKey.F12)
            return "F" + (code - (int)VirtualKey.F1 + 1);
        if (code is >= 'A' and <= 'Z') return ((char)code).ToString();
        if (code is >= '0' and <= '9') return ((char)code).ToString();
        if (code >= (int)VirtualKey.NumberPad0 && code <= (int)VirtualKey.NumberPad9)
            return ((char)('0' + (code - (int)VirtualKey.NumberPad0))).ToString();
        return null;
    }
}
