using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;

namespace SnapCat.App.Services;

public sealed class ThemeService
{
    private const string DefaultThemeId = "ocean-blue";

    private static readonly IReadOnlyDictionary<string, ThemePalette> Palettes =
        new Dictionary<string, ThemePalette>(StringComparer.OrdinalIgnoreCase)
        {
            ["ocean-blue"] = new(
                "ocean-blue",
                "深海蓝",
                "#B81B2230",
                "#B84B5C76",
                "#B81B1F28",
                "#AA2A3240",
                "#A63B4B61",
                "#FF2B2B2B",
                "#FF333A46",
                "#FF252525",
                "#FFF3F4F6",
                "#FFCBD5E1",
                "#FF94A3B8",
                "#FF374151",
                "#FF3A4659",
                "#FF2B3442",
                "#FF4B5563",
                "#FF0C89F3",
                "#FF38BDF8",
                "#220C89F3",
                "#330C89F3",
                "#FF111827",
                "#FF4B5563",
                "#664B5563",
                "#FF38BDF8",
                "#FF0C89F3",
                "#FF0F172A"),
            ["forest-green"] = new(
                "forest-green",
                "松林绿",
                "#B81B261F",
                "#B84C6A56",
                "#B81A211D",
                "#AA27372E",
                "#A63B5A4A",
                "#FF262A28",
                "#FF313A35",
                "#FF242826",
                "#FFF3F4F6",
                "#FFD2E7DA",
                "#FF9CB8A9",
                "#FF33423B",
                "#FF395048",
                "#FF29342F",
                "#FF4D665B",
                "#FF22C55E",
                "#FF86EFAC",
                "#1F22C55E",
                "#3322C55E",
                "#FF101915",
                "#FF557564",
                "#66557564",
                "#FF86EFAC",
                "#FFBBF7D0",
                "#FF0B1310"),
            ["teal-cyan"] = new(
                "teal-cyan",
                "青岚色",
                "#B8172527",
                "#B8456A70",
                "#B8182023",
                "#AA24393D",
                "#A63A5960",
                "#FF252B2C",
                "#FF2F3B3D",
                "#FF23292A",
                "#FFF3F4F6",
                "#FFCFE7E8",
                "#FF9DB9BC",
                "#FF344143",
                "#FF3A5052",
                "#FF293436",
                "#FF4F676A",
                "#FF14B8A6",
                "#FF67E8F9",
                "#1F14B8A6",
                "#3314B8A6",
                "#FF0E1719",
                "#FF557073",
                "#66557073",
                "#FF67E8F9",
                "#FFA5F3FC",
                "#FF0A1213"),
            ["sunset-orange"] = new(
                "sunset-orange",
                "落日橙",
                "#B8271E1B",
                "#B8775C4B",
                "#B8231D1A",
                "#AA423026",
                "#A66A4A39",
                "#FF2E2A28",
                "#FF3D332F",
                "#FF292523",
                "#FFF7F4F2",
                "#FFEAD4C7",
                "#FFC1A691",
                "#FF463730",
                "#FF58443A",
                "#FF382C27",
                "#FF75584A",
                "#FFF97316",
                "#FFFDBA74",
                "#1FF97316",
                "#33F97316",
                "#FF19110E",
                "#FF7A5A4A",
                "#667A5A4A",
                "#FFFDBA74",
                "#FFFED7AA",
                "#FF16100C"),
            ["ruby-red"] = new(
                "ruby-red",
                "绯红色",
                "#B8261B1F",
                "#B8754E59",
                "#B8221A1D",
                "#AA3E262E",
                "#A6643B47",
                "#FF2D272A",
                "#FF3C2F35",
                "#FF292325",
                "#FFF7F3F5",
                "#FFE7D0D8",
                "#FFBE99A7",
                "#FF443238",
                "#FF573E48",
                "#FF36292E",
                "#FF71515C",
                "#FFEF4444",
                "#FFFDA4AF",
                "#1FEF4444",
                "#33EF4444",
                "#FF1A1013",
                "#FF775661",
                "#66775661",
                "#FFFDA4AF",
                "#FFFBCFE8",
                "#FF170E10"),
            ["amber-gold"] = new(
                "amber-gold",
                "琥珀金",
                "#B8282318",
                "#B87D6A43",
                "#B8241F17",
                "#AA45351F",
                "#A66A5230",
                "#FF2F2A23",
                "#FF40362A",
                "#FF2A251F",
                "#FFF8F5ED",
                "#FFE9DCC0",
                "#FFC5B088",
                "#FF463A2D",
                "#FF5C4B37",
                "#FF382F25",
                "#FF76614A",
                "#FFF59E0B",
                "#FFFCD34D",
                "#1FF59E0B",
                "#33F59E0B",
                "#FF1A140B",
                "#FF7A6848",
                "#667A6848",
                "#FFFCD34D",
                "#FFFDE68A",
                "#FF171208"),
            ["rose-pink"] = new(
                "rose-pink",
                "玫瑰粉",
                "#B8261C24",
                "#B8775367",
                "#B8221B22",
                "#AA402633",
                "#A6663E52",
                "#FF2D272B",
                "#FF3B3036",
                "#FF292326",
                "#FFF8F3F7",
                "#FFE9D0DE",
                "#FFC49EB4",
                "#FF44333B",
                "#FF57404C",
                "#FF362A2F",
                "#FF70535F",
                "#FFEC4899",
                "#FFF9A8D4",
                "#1FEC4899",
                "#33EC4899",
                "#FF191017",
                "#FF77586B",
                "#6677586B",
                "#FFF9A8D4",
                "#FFFBCFE8",
                "#FF170E14"),
            ["indigo-night"] = new(
                "indigo-night",
                "靛夜紫",
                "#B81E2030",
                "#B85A5E83",
                "#B81C1D29",
                "#AA2E3147",
                "#A6484B68",
                "#FF292A31",
                "#FF343642",
                "#FF25262C",
                "#FFF4F4F8",
                "#FFD7D9EA",
                "#FFA7ACC7",
                "#FF3A3C48",
                "#FF45495C",
                "#FF2D2F3A",
                "#FF5A6077",
                "#FF6366F1",
                "#FFA5B4FC",
                "#1F6366F1",
                "#336366F1",
                "#FF11131D",
                "#FF60667C",
                "#6660667C",
                "#FFA5B4FC",
                "#FFC7D2FE",
                "#FF0D1018")
        };

    public IReadOnlyList<ThemeOption> GetOptions()
    {
        return Palettes.Values
            .Select(static palette => new ThemeOption(palette.Id, palette.Label))
            .ToArray();
    }

    public string NormalizeThemeId(string? themeId)
    {
        return !string.IsNullOrWhiteSpace(themeId) && Palettes.ContainsKey(themeId)
            ? themeId
            : DefaultThemeId;
    }

    public string GetThemeLabel(string? themeId)
    {
        var palette = Palettes[NormalizeThemeId(themeId)];
        return palette.Label;
    }

    public string ApplyTheme(WpfApplication application, string? themeId)
    {
        var palette = Palettes[NormalizeThemeId(themeId)];
        var resources = application.Resources;

        SetBrush(resources, "Theme.Brush.WindowBackground", palette.WindowBackground);
        SetBrush(resources, "Theme.Brush.WindowBorder", palette.WindowBorder);
        SetBrush(resources, "Theme.Brush.CardBackground", palette.CardBackground);
        SetBrush(resources, "Theme.Brush.CardBorder", palette.CardBorder);
        SetBrush(resources, "Theme.Brush.SurfaceBorder", palette.SurfaceBorder);
        SetBrush(resources, "Theme.Brush.SurfaceBackground", palette.SurfaceBackground);
        SetBrush(resources, "Theme.Brush.SurfaceAltBackground", palette.SurfaceAltBackground);
        SetBrush(resources, "Theme.Brush.ReadOnlyBackground", palette.ReadOnlyBackground);
        SetBrush(resources, "Theme.Brush.TextPrimary", palette.TextPrimary);
        SetBrush(resources, "Theme.Brush.TextSecondary", palette.TextSecondary);
        SetBrush(resources, "Theme.Brush.TextMuted", palette.TextMuted);
        SetBrush(resources, "Theme.Brush.ButtonBackground", palette.ButtonBackground);
        SetBrush(resources, "Theme.Brush.ButtonHoverBackground", palette.ButtonHoverBackground);
        SetBrush(resources, "Theme.Brush.ButtonPressedBackground", palette.ButtonPressedBackground);
        SetBrush(resources, "Theme.Brush.ButtonBorder", palette.ButtonBorder);
        SetBrush(resources, "Theme.Brush.Accent", palette.Accent);
        SetBrush(resources, "Theme.Brush.AccentBorder", palette.AccentBorder);
        SetBrush(resources, "Theme.Brush.AccentSoft", palette.AccentSoft);
        SetBrush(resources, "Theme.Brush.AccentSoftPressed", palette.AccentSoftPressed);
        SetBrush(resources, "Theme.Brush.ScrollTrack", palette.ScrollTrack);
        SetBrush(resources, "Theme.Brush.ScrollThumb", palette.ScrollThumb);
        SetBrush(resources, "Theme.Brush.Divider", palette.Divider);
        SetBrush(resources, "Theme.Brush.Highlight", palette.Highlight);
        SetBrush(resources, "Theme.Brush.HighlightAlt", palette.HighlightAlt);
        var accent = ParseColor(palette.Accent);
        var highlight = ParseColor(palette.HighlightAlt);
        SetColor(resources, "Theme.Color.Accent", accent);
        SetColor(resources, "Theme.Color.HighlightAlt", highlight);
        SetColor(resources, "Theme.Color.Shadow", palette.ShadowColor);
        resources["Theme.Image.AppLogo"] = ThemedLogoService.CreateLogoImage(accent, highlight);
        resources["Theme.CurrentLabel"] = palette.Label;

        return palette.Id;
    }

    private static void SetBrush(ResourceDictionary resources, string key, string hex)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        resources[key] = brush;
    }

    private static void SetColor(ResourceDictionary resources, string key, string hex)
    {
        resources[key] = ParseColor(hex);
    }

    private static void SetColor(ResourceDictionary resources, string key, System.Windows.Media.Color color)
    {
        resources[key] = color;
    }

    private static System.Windows.Media.Color ParseColor(string hex)
    {
        return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
    }

    public sealed record ThemeOption(string Id, string Label);

    private sealed record ThemePalette(
        string Id,
        string Label,
        string WindowBackground,
        string WindowBorder,
        string CardBackground,
        string CardBorder,
        string SurfaceBorder,
        string SurfaceBackground,
        string SurfaceAltBackground,
        string ReadOnlyBackground,
        string TextPrimary,
        string TextSecondary,
        string TextMuted,
        string ButtonBackground,
        string ButtonHoverBackground,
        string ButtonPressedBackground,
        string ButtonBorder,
        string Accent,
        string AccentBorder,
        string AccentSoft,
        string AccentSoftPressed,
        string ScrollTrack,
        string ScrollThumb,
        string Divider,
        string Highlight,
        string HighlightAlt,
        string ShadowColor);
}
