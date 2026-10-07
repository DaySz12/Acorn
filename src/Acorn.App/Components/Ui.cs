namespace Acorn.App.Components;

/// <summary>Presentation-only mappings (labels, icons, CSS classes). No business rules.</summary>
internal static class Ui
{
    public static string TypeIcon(string type) => type switch
    {
        "login" => "login",
        "note" => "note",
        _ => "server",
    };

    public static string TypeLabel(string type) => type switch
    {
        "login" => "บัญชีล็อกอิน",
        "note" => "โน้ต",
        _ => "เซิร์ฟเวอร์",
    };

    public static string EnvClass(string env) => env.ToLowerInvariant() switch
    {
        "dev" or "development" or "local" => "env-dev",
        "staging" or "stage" or "uat" or "qa" or "test" => "env-staging",
        "prod" or "production" or "live" => "env-prod",
        _ => "env-other",
    };

    public static string ThemeLabel(string theme) => theme switch
    {
        "light" => "สว่าง",
        "dark" => "มืด",
        _ => "ตามระบบ",
    };

    public static string ShortcutLabel => OperatingSystem.IsMacOS() ? "⌘K" : "Ctrl K";

    public static string LocalTime(DateTime utc) =>
        utc == default ? "—" : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("d MMM yyyy HH:mm");
}
