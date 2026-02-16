using System.Windows;
using System.Windows.Media;

namespace Proc;

public record ColorTheme
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";

    // Backgrounds
    public Color Bg { get; init; }
    public Color BgTransparent { get; init; } // MainWindow semi-transparent bg
    public Color Surface { get; init; }
    public Color SurfaceAlt { get; init; }

    // Borders
    public Color Border { get; init; }

    // Accent
    public Color Accent { get; init; }

    // Text
    public Color Text { get; init; }
    public Color SubText { get; init; }
    public Color DimText { get; init; }

    // Active indicator
    public Color ActiveTime { get; init; }
    public Color ActiveAccent { get; init; }

    // Interactive
    public Color Hover { get; init; }
    public Color Pressed { get; init; }
    public Color CheckBg { get; init; }
    public Color CheckBorder { get; init; }

    // Inactive/disabled
    public Color InactiveText { get; init; }
    public Color BlackedOutText { get; init; }

    public void Apply()
    {
        var res = Application.Current.Resources;
        res["ThemeBg"] = new SolidColorBrush(Bg);
        res["ThemeBgTransparent"] = new SolidColorBrush(BgTransparent);
        res["ThemeSurface"] = new SolidColorBrush(Surface);
        res["ThemeSurfaceAlt"] = new SolidColorBrush(SurfaceAlt);
        res["ThemeBorder"] = new SolidColorBrush(Border);
        res["ThemeAccent"] = new SolidColorBrush(Accent);
        res["ThemeText"] = new SolidColorBrush(Text);
        res["ThemeSubText"] = new SolidColorBrush(SubText);
        res["ThemeDimText"] = new SolidColorBrush(DimText);
        res["ThemeActiveTime"] = new SolidColorBrush(ActiveTime);
        res["ThemeActiveAccent"] = new SolidColorBrush(ActiveAccent);
        res["ThemeHover"] = new SolidColorBrush(Hover);
        res["ThemePressed"] = new SolidColorBrush(Pressed);
        res["ThemeCheckBg"] = new SolidColorBrush(CheckBg);
        res["ThemeCheckBorder"] = new SolidColorBrush(CheckBorder);
        res["ThemeInactiveText"] = new SolidColorBrush(InactiveText);
        res["ThemeBlackedOutText"] = new SolidColorBrush(BlackedOutText);
        // Color values (for non-brush usage)
        res["ThemeAccentColor"] = Accent;
        res["ThemeBgColor"] = Bg;
    }

    public static ColorTheme[] All => [
        DarkOrange, DarkBlue, DarkGreen, DarkPurple,
        LightOrange, LightBlue, LightGreen, LightPurple
    ];

    public static ColorTheme GetByName(string name) =>
        All.FirstOrDefault(t => t.Name == name) ?? DarkOrange;

    public static ColorTheme FromColors(Color bg, Color accent)
    {
        // Determine dark vs light based on relative luminance
        double lum = 0.2126 * bg.R / 255.0 + 0.7152 * bg.G / 255.0 + 0.0722 * bg.B / 255.0;
        bool isDark = lum < 0.5;

        if (isDark)
        {
            return new ColorTheme
            {
                Name = "Custom", DisplayName = "Custom",
                Bg = bg,
                BgTransparent = Color.FromArgb(0xE0, bg.R, bg.G, bg.B),
                Surface = Lighten(bg, 0.04),
                SurfaceAlt = Lighten(bg, 0.07),
                Border = Lighten(bg, 0.12),
                Accent = accent,
                Text = Color.FromRgb(0xD0, 0xD0, 0xD0),
                SubText = Color.FromRgb(0xA0, 0xA0, 0xA0),
                DimText = Color.FromRgb(0x70, 0x70, 0x70),
                ActiveTime = Color.FromRgb(0x80, 0xFF, 0x80),
                ActiveAccent = accent,
                Hover = Lighten(bg, 0.15),
                Pressed = Lighten(bg, 0.22),
                CheckBg = Lighten(bg, 0.10),
                CheckBorder = Lighten(bg, 0.22),
                InactiveText = Color.FromRgb(0x60, 0x60, 0x60),
                BlackedOutText = Color.FromRgb(0x50, 0x50, 0x50),
            };
        }
        else
        {
            return new ColorTheme
            {
                Name = "Custom", DisplayName = "Custom",
                Bg = bg,
                BgTransparent = Color.FromArgb(0xE8, bg.R, bg.G, bg.B),
                Surface = Color.FromRgb(0xFF, 0xFF, 0xFF),
                SurfaceAlt = Darken(bg, 0.04),
                Border = Darken(bg, 0.12),
                Accent = accent,
                Text = Color.FromRgb(0x1E, 0x1E, 0x1E),
                SubText = Color.FromRgb(0x60, 0x60, 0x60),
                DimText = Color.FromRgb(0x90, 0x90, 0x90),
                ActiveTime = Color.FromRgb(0x00, 0x88, 0x00),
                ActiveAccent = accent,
                Hover = Darken(bg, 0.06),
                Pressed = Darken(bg, 0.12),
                CheckBg = Color.FromRgb(0xFF, 0xFF, 0xFF),
                CheckBorder = Color.FromRgb(0xAA, 0xAA, 0xAA),
                InactiveText = Color.FromRgb(0xA0, 0xA0, 0xA0),
                BlackedOutText = Color.FromRgb(0xC0, 0xC0, 0xC0),
            };
        }
    }

    public static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6 &&
            byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
            byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
            byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out byte b))
        {
            return Color.FromRgb(r, g, b);
        }
        return Color.FromRgb(0x1E, 0x1E, 0x1E); // fallback
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static bool IsValidHex(string hex)
    {
        hex = hex.TrimStart('#');
        return hex.Length == 6 &&
               byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out _) &&
               byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out _) &&
               byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out _);
    }

    private static byte ClampByte(int v) => (byte)Math.Clamp(v, 0, 255);

    private static Color Lighten(Color c, double amount)
    {
        int delta = (int)(255 * amount);
        return Color.FromRgb(ClampByte(c.R + delta), ClampByte(c.G + delta), ClampByte(c.B + delta));
    }

    private static Color Darken(Color c, double amount)
    {
        int delta = (int)(255 * amount);
        return Color.FromRgb(ClampByte(c.R - delta), ClampByte(c.G - delta), ClampByte(c.B - delta));
    }

    // ─── Dark themes ───

    public static ColorTheme DarkOrange { get; } = new()
    {
        Name = "Dark-Orange", DisplayName = "Dark Orange",
        Bg = Color.FromRgb(0x1E, 0x1E, 0x1E),
        BgTransparent = Color.FromArgb(0xE0, 0x20, 0x20, 0x20),
        Surface = Color.FromRgb(0x25, 0x25, 0x25),
        SurfaceAlt = Color.FromRgb(0x2A, 0x2A, 0x2A),
        Border = Color.FromRgb(0x33, 0x33, 0x33),
        Accent = Color.FromRgb(0xFF, 0x8C, 0x00),
        Text = Color.FromRgb(0xD0, 0xD0, 0xD0),
        SubText = Color.FromRgb(0xA0, 0xA0, 0xA0),
        DimText = Color.FromRgb(0x70, 0x70, 0x70),
        ActiveTime = Color.FromRgb(0x80, 0xFF, 0x80),
        ActiveAccent = Color.FromRgb(0xFF, 0x8C, 0x00),
        Hover = Color.FromRgb(0x44, 0x44, 0x44),
        Pressed = Color.FromRgb(0x55, 0x55, 0x55),
        CheckBg = Color.FromRgb(0x33, 0x33, 0x33),
        CheckBorder = Color.FromRgb(0x55, 0x55, 0x55),
        InactiveText = Color.FromRgb(0x60, 0x60, 0x60),
        BlackedOutText = Color.FromRgb(0x50, 0x50, 0x50),
    };

    public static ColorTheme DarkBlue { get; } = DarkOrange with
    {
        Name = "Dark-Blue", DisplayName = "Dark Blue",
        Accent = Color.FromRgb(0x4E, 0xA8, 0xF0),
        ActiveAccent = Color.FromRgb(0x4E, 0xA8, 0xF0),
    };

    public static ColorTheme DarkGreen { get; } = DarkOrange with
    {
        Name = "Dark-Green", DisplayName = "Dark Green",
        Accent = Color.FromRgb(0x4E, 0xC9, 0x6B),
        ActiveAccent = Color.FromRgb(0x4E, 0xC9, 0x6B),
    };

    public static ColorTheme DarkPurple { get; } = DarkOrange with
    {
        Name = "Dark-Purple", DisplayName = "Dark Purple",
        Accent = Color.FromRgb(0xB0, 0x7E, 0xF0),
        ActiveAccent = Color.FromRgb(0xB0, 0x7E, 0xF0),
    };

    // ─── Light themes ───

    private static ColorTheme LightBase { get; } = new()
    {
        Bg = Color.FromRgb(0xF0, 0xF0, 0xF0),
        BgTransparent = Color.FromArgb(0xE8, 0xF0, 0xF0, 0xF0),
        Surface = Color.FromRgb(0xFF, 0xFF, 0xFF),
        SurfaceAlt = Color.FromRgb(0xE8, 0xE8, 0xE8),
        Border = Color.FromRgb(0xD0, 0xD0, 0xD0),
        Text = Color.FromRgb(0x1E, 0x1E, 0x1E),
        SubText = Color.FromRgb(0x60, 0x60, 0x60),
        DimText = Color.FromRgb(0x90, 0x90, 0x90),
        ActiveTime = Color.FromRgb(0x00, 0x88, 0x00),
        Hover = Color.FromRgb(0xE0, 0xE0, 0xE0),
        Pressed = Color.FromRgb(0xD0, 0xD0, 0xD0),
        CheckBg = Color.FromRgb(0xFF, 0xFF, 0xFF),
        CheckBorder = Color.FromRgb(0xAA, 0xAA, 0xAA),
        InactiveText = Color.FromRgb(0xA0, 0xA0, 0xA0),
        BlackedOutText = Color.FromRgb(0xC0, 0xC0, 0xC0),
    };

    public static ColorTheme LightOrange { get; } = LightBase with
    {
        Name = "Light-Orange", DisplayName = "Light Orange",
        Accent = Color.FromRgb(0xE0, 0x78, 0x00),
        ActiveAccent = Color.FromRgb(0xE0, 0x78, 0x00),
    };

    public static ColorTheme LightBlue { get; } = LightBase with
    {
        Name = "Light-Blue", DisplayName = "Light Blue",
        Accent = Color.FromRgb(0x1A, 0x7A, 0xD4),
        ActiveAccent = Color.FromRgb(0x1A, 0x7A, 0xD4),
    };

    public static ColorTheme LightGreen { get; } = LightBase with
    {
        Name = "Light-Green", DisplayName = "Light Green",
        Accent = Color.FromRgb(0x2E, 0x9E, 0x48),
        ActiveAccent = Color.FromRgb(0x2E, 0x9E, 0x48),
    };

    public static ColorTheme LightPurple { get; } = LightBase with
    {
        Name = "Light-Purple", DisplayName = "Light Purple",
        Accent = Color.FromRgb(0x88, 0x56, 0xD0),
        ActiveAccent = Color.FromRgb(0x88, 0x56, 0xD0),
    };
}
