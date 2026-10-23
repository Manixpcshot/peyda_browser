namespace PeydaBrowser.Core.Shortcuts;

/// <summary>Commands that can be bound to a keyboard shortcut. The set of
/// bindings is data (<see cref="DefaultShortcuts"/>) so user remapping can be
/// added later without touching input-handling code.</summary>
public enum BrowserCommand
{
    NewTab,
    CloseTab,
    ReopenClosedTab,
    FocusAddressBar,
    Reload,
    HardReload,
    Back,
    Forward,
    AddBookmark,
    OpenHistory,
    OpenDownloads,
    NewPrivateWindow,
    NextTab,
    PreviousTab,
    SelectTab,
    SelectLastTab,
    ToggleFullscreen,
    OpenDevTools,
    ZoomIn,
    ZoomOut,
    ZoomReset,
    Print
}

/// <summary>A normalized keyboard gesture such as Ctrl+Shift+T.</summary>
public readonly record struct ShortcutGesture(bool Ctrl, bool Alt, bool Shift, string Key)
{
    public static ShortcutGesture Parse(string text)
    {
        if (!TryParse(text, out var gesture))
            throw new FormatException($"'{text}' is not a valid keyboard gesture.");
        return gesture;
    }

    public static bool TryParse(string? text, out ShortcutGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var ctrl = false;
        var alt = false;
        var shift = false;
        string? key = null;

        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    if (ctrl) return false;
                    ctrl = true;
                    break;
                case "alt":
                    if (alt) return false;
                    alt = true;
                    break;
                case "shift":
                    if (shift) return false;
                    shift = true;
                    break;
                default:
                    if (key is not null) return false;
                    key = NormalizeKey(part);
                    if (key is null) return false;
                    break;
            }
        }

        if (key is null) return false;
        gesture = new ShortcutGesture(ctrl, alt, shift, key);
        return true;
    }

    private static string? NormalizeKey(string part)
    {
        if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
            return char.ToUpperInvariant(part[0]).ToString();
        return part.ToLowerInvariant() switch
        {
            "tab" => "Tab",
            "left" => "Left",
            "right" => "Right",
            "up" => "Up",
            "down" => "Down",
            "escape" or "esc" => "Escape",
            "enter" or "return" => "Enter",
            "plus" or "+" => "Plus",
            "minus" or "-" => "Minus",
            var f when f.Length >= 2 && f[0] == 'f' && int.TryParse(f[1..], out var n) && n is >= 1 and <= 24 => "F" + n,
            _ => null
        };
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(Key);
        return string.Join("+", parts);
    }
}

public sealed record ShortcutDefinition(BrowserCommand Command, ShortcutGesture Gesture, int Argument = 0);

public static class DefaultShortcuts
{
    public static IReadOnlyList<ShortcutDefinition> All { get; } = Build();

    private static List<ShortcutDefinition> Build()
    {
        var list = new List<ShortcutDefinition>
        {
            new(BrowserCommand.NewTab, ShortcutGesture.Parse("Ctrl+T")),
            new(BrowserCommand.CloseTab, ShortcutGesture.Parse("Ctrl+W")),
            new(BrowserCommand.ReopenClosedTab, ShortcutGesture.Parse("Ctrl+Shift+T")),
            new(BrowserCommand.FocusAddressBar, ShortcutGesture.Parse("Ctrl+L")),
            new(BrowserCommand.FocusAddressBar, ShortcutGesture.Parse("Alt+D")),
            new(BrowserCommand.Reload, ShortcutGesture.Parse("Ctrl+R")),
            new(BrowserCommand.Reload, ShortcutGesture.Parse("F5")),
            new(BrowserCommand.HardReload, ShortcutGesture.Parse("Ctrl+Shift+R")),
            new(BrowserCommand.Back, ShortcutGesture.Parse("Alt+Left")),
            new(BrowserCommand.Forward, ShortcutGesture.Parse("Alt+Right")),
            new(BrowserCommand.AddBookmark, ShortcutGesture.Parse("Ctrl+D")),
            new(BrowserCommand.OpenHistory, ShortcutGesture.Parse("Ctrl+H")),
            new(BrowserCommand.OpenDownloads, ShortcutGesture.Parse("Ctrl+J")),
            new(BrowserCommand.NewPrivateWindow, ShortcutGesture.Parse("Ctrl+Shift+N")),
            new(BrowserCommand.NextTab, ShortcutGesture.Parse("Ctrl+Tab")),
            new(BrowserCommand.PreviousTab, ShortcutGesture.Parse("Ctrl+Shift+Tab")),
            new(BrowserCommand.ToggleFullscreen, ShortcutGesture.Parse("F11")),
            new(BrowserCommand.OpenDevTools, ShortcutGesture.Parse("F12")),
            new(BrowserCommand.ZoomIn, ShortcutGesture.Parse("Ctrl+Plus")),
            new(BrowserCommand.ZoomOut, ShortcutGesture.Parse("Ctrl+Minus")),
            new(BrowserCommand.ZoomReset, ShortcutGesture.Parse("Ctrl+0")),
            new(BrowserCommand.Print, ShortcutGesture.Parse("Ctrl+P"))
        };
        for (var i = 1; i <= 8; i++)
            list.Add(new ShortcutDefinition(BrowserCommand.SelectTab, ShortcutGesture.Parse($"Ctrl+{i}"), i - 1));
        list.Add(new ShortcutDefinition(BrowserCommand.SelectLastTab, ShortcutGesture.Parse("Ctrl+9")));
        return list;
    }

    /// <summary>Returns true when no gesture is bound to two different commands.</summary>
    public static bool HasConflicts(IEnumerable<ShortcutDefinition> definitions)
    {
        var seen = new Dictionary<ShortcutGesture, (BrowserCommand, int)>();
        foreach (var d in definitions)
        {
            if (seen.TryGetValue(d.Gesture, out var existing) && existing != (d.Command, d.Argument))
                return true;
            seen[d.Gesture] = (d.Command, d.Argument);
        }
        return false;
    }
}
