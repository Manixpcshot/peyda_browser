using System.Text.Json.Serialization;

namespace PeydaBrowser.Theming;

/// <summary>
/// Serializable theme definition. Field names in JSON are camelCase to match
/// the documented theme format in docs/THEMING.md.
/// </summary>
public sealed class ThemeDefinition
{
    [JsonPropertyName("peydaThemeVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("name")] public string Name { get; set; } = "Peyda Dark";
    [JsonPropertyName("mode")] public string Mode { get; set; } = "dark";
    [JsonPropertyName("accent")] public string Accent { get; set; } = "#7C3AED";
    [JsonPropertyName("background")] public string Background { get; set; } = "#0F1117";
    [JsonPropertyName("surface")] public string Surface { get; set; } = "#171A23";
    [JsonPropertyName("toolbar")] public string Toolbar { get; set; } = "#151821";
    [JsonPropertyName("textPrimary")] public string TextPrimary { get; set; } = "#F8FAFC";
    [JsonPropertyName("textSecondary")] public string TextSecondary { get; set; } = "#94A3B8";
    [JsonPropertyName("border")] public string Border { get; set; } = "#2A2F3F";
    [JsonPropertyName("tabActive")] public string TabActive { get; set; } = "#1E2230";
    [JsonPropertyName("tabInactive")] public string TabInactive { get; set; } = "#12151F";
    [JsonPropertyName("tabStyle")] public string TabStyle { get; set; } = "rounded";
    [JsonPropertyName("tabPosition")] public string TabPosition { get; set; } = "top";
    [JsonPropertyName("density")] public string Density { get; set; } = "normal";
    [JsonPropertyName("sidebarEnabled")] public bool SidebarEnabled { get; set; } = true;
    [JsonPropertyName("transparency")] public double Transparency { get; set; } = 0.95;
    [JsonPropertyName("cornerRadius")] public double CornerRadius { get; set; } = 8;
    [JsonPropertyName("toolbarHeight")] public double ToolbarHeight { get; set; } = 48;
    [JsonPropertyName("uiFontFamily")] public string UiFontFamily { get; set; } = "Segoe UI Variable Text";

    public ThemeDefinition Clone() => (ThemeDefinition)MemberwiseClone();
}
