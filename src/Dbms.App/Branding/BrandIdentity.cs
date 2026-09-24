namespace Dbms.App.Branding;

/// <summary>
/// Shared identity primitives for the Orbit application suite.
/// Individual tools should keep their product name here while reusing the same
/// shell, tone, color tokens, and interaction language.
/// </summary>
public static class BrandIdentity
{
    public const string SuiteName = "Orbit";
    public const string ProductName = "DataBridge";
    public const string ProductDescriptor = "Data operations";
    public const string WindowTitle = $"{SuiteName} · {ProductName}";
    public const string Tagline = "Clarity for every data move.";
    public const string ShortTagline = "Compare. Understand. Move safely.";
    public const string WorkspaceLabel = "ORBIT WORKSPACE";
    public const string SafetyLabel = "GUARDRAILS ACTIVE";

    public const string Ink = "#14213D";
    public const string InkSoft = "#42526E";
    public const string Canvas = "#F7F8FC";
    public const string Panel = "#FFFFFF";
    public const string PanelSubtle = "#F1F3F8";
    public const string Line = "#E3E7F0";
    public const string Accent = "#5B5CE2";
    public const string AccentDark = "#4142B7";
    public const string Mint = "#2DBE9B";
    public const string Coral = "#F2766B";
    public const string Amber = "#E4A72C";
}
