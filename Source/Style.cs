using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

[StaticConstructorOnStartup]
public static class Style
{
    // the color picked in the settings for lines and highlights
    public static Color Accent => JustDesktopMod.Settings.lineColor;
    public static Color Faded(float alpha) => new(Accent.r, Accent.g, Accent.b, alpha);

    private static bool PerIcon => JustDesktopMod.Settings.back == BackMode.Icons;

    // a filled box, rounded when each icon has its own background
    public static void Box(Rect rect, Color color)
    {
        if (PerIcon)
        {
            GUI.color = color;
            Widgets.DrawAtlas(rect, Round);
            GUI.color = Color.white;
        }
        else
        {
            Widgets.DrawBoxSolid(rect, color);
        }
    }

    // the background of one button when each icon has its own, and what the mouse over it does:
    // lighter, or with its own background that background fully solid or gone
    public static void ButtonBack(Rect rect, bool hovered, bool rounded = false)
    {
        Settings s = JustDesktopMod.Settings;
        if (PerIcon)
        {
            Color back = s.backColor;
            if (hovered && s.hoverLook == HoverLook.Solid)
            {
                back.a = 1f;
            }
            else if (hovered && s.hoverLook == HoverLook.Clear)
            {
                back.a = 0f;
            }
            if (back.a > 0f)
            {
                Box(rect, back);
            }
        }
        if (hovered && (s.hoverLook == HoverLook.Lighter || !PerIcon))
        {
            if (rounded && !PerIcon)
            {
                GUI.color = Hover;
                Widgets.DrawAtlas(rect, Round);
                GUI.color = Color.white;
            }
            else
            {
                Box(rect, Hover);
            }
        }
    }

    // the small label with the name and shortcut, above the button
    public static void NameLabel(MainButtonDef def, Rect button)
    {
        string text = def.LabelCap;
        if (def.hotKey != null && def.hotKey.MainKey != KeyCode.None)
        {
            text += "  " + def.hotKey.MainKeyLabel;
        }
        Label(text, button);
    }

    // a small rounded label centered above something
    public static void Label(string text, Rect button)
    {
        GameFont old = Verse.Text.Font;
        Verse.Text.Font = GameFont.Small;
        float w = Verse.Text.CalcSize(text).x + 20f;
        Rect rect = new Rect(button.center.x - w / 2f, button.y - 34f, w, 26f);
        rect.x = Mathf.Clamp(rect.x, 4f, UI.screenWidth - w - 4f);
        Verse.Text.Font = old;
        // in a window of its own on the top layer, so it shows over the windows that are open
        Find.WindowStack.ImmediateWindow(73914562, rect, WindowLayer.Super, () =>
        {
            Rect r = rect.AtZero();
            Verse.Text.Font = GameFont.Small;
            GUI.color = DockLabel;
            Widgets.DrawAtlas(r, Round);
            GUI.color = Text;
            Verse.Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, text);
            Verse.Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }, doBackground: false, absorbInputAroundWindow: false, shadowAlpha: 0f);
    }
    public static readonly Color Hover = new(1f, 1f, 1f, 0.07f);
    // with a text color picked, text is tinted to it (see TextTint) so ours is plain white, the dimmer kinds see-through
    public static Color Text => TextCustom ? Color.white : new Color(0.86f, 0.9f, 0.94f, 1f);
    public static Color Dim => TextCustom ? new Color(1f, 1f, 1f, 0.6f) : new Color(0.5f, 0.55f, 0.6f, 1f);
    public static Color Disabled => TextCustom ? new Color(1f, 1f, 1f, 0.3f) : new Color(0.5f, 0.55f, 0.6f, 0.45f);
    private static bool TextCustom => TextColor.On;
    public static readonly Color Divider = new(1f, 1f, 1f, 0.1f);
    public static readonly Color Title = new(0.075f, 0.095f, 0.12f, 1f);
    public static readonly Color CloseHover = new(0.84f, 0.24f, 0.27f, 1f);
    public static readonly Color DockPanel = new(0.07f, 0.085f, 0.105f, 0.82f);
    public static readonly Color DockLabel = new(0.06f, 0.07f, 0.09f, 0.94f);
    public static readonly Color MacClose = new(1f, 0.37f, 0.34f, 1f);
    public static readonly Color MacMaximize = new(0.16f, 0.79f, 0.25f, 1f);

    private static Texture2D _start;

    public static Texture2D Start => _start ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Start");
    private static Texture2D _generic;
    public static Texture2D Generic => _generic ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Tabs/Generic");
    private static Texture2D _maximize;
    public static Texture2D Maximize => _maximize ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Window/Maximize");
    private static Texture2D _restore;
    public static Texture2D Restore => _restore ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Window/Restore");
    private static Texture2D _close;
    public static Texture2D Close => _close ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Window/Close");
    private static Texture2D _round;
    public static Texture2D Round => _round ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Round");
    private static Texture2D _circle;
    public static Texture2D Circle => _circle ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Circle");
    private static Texture2D _roundLine;
    public static Texture2D RoundLine => _roundLine ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/RoundLine");
    public static readonly Color Border = new(1f, 1f, 1f, 0.09f);

    private static readonly Dictionary<MainButtonDef, (Texture2D tex, bool line)> taskbarIcons = new();
    private static readonly Dictionary<MainButtonDef, (Texture2D tex, bool line)> dockIcons = new();

    // after a choice in the icon window or new custom icons
    public static void ClearCache()
    {
        taskbarIcons.Clear();
        dockIcons.Clear();
    }

    private static (Texture2D tex, bool line)? Chosen(Dictionary<string, string> choices, MainButtonDef def)
    {
        if (choices != null && choices.TryGetValue(def.defName, out string key) && IconLibrary.Get(key) is { } icon)
        {
            return (icon.Texture, icon.Line);
        }
        return null;
    }

    // the icon the player picked for the taskbar, otherwise our line icon (the hexagon for Architect),
    // the tab's own icon for mod tabs that have one, or a generic one
    public static (Texture2D tex, bool line) TaskbarIconFor(MainButtonDef def)
    {
        if (!taskbarIcons.TryGetValue(def, out var icon))
        {
            icon = Chosen(JustDesktopMod.Settings.taskbarIcons, def)
                ?? (def == MainButtonDefOf.Architect ? (Start, true) : DefaultLine(def));
            taskbarIcons[def] = icon;
        }
        return icon;
    }

    // the icon the player picked for the dock, otherwise the colored dock icon, the tab's own icon, or the line icon
    // (the menu uses the line icon, the colored one stands out too much at the end of the dock)
    public static (Texture2D tex, bool line) DockIconFor(MainButtonDef def)
    {
        if (!dockIcons.TryGetValue(def, out var icon))
        {
            Texture2D dock = def == MainButtonDefOf.Menu ? null : ContentFinder<Texture2D>.Get("UI/JustDesktop/Dock/" + def.defName, false);
            Texture2D line = ContentFinder<Texture2D>.Get("UI/JustDesktop/Tabs/" + def.defName, false);
            icon = Chosen(JustDesktopMod.Settings.dockIcons, def)
                ?? (dock != null ? (dock, false) : line == null && def.Icon != null ? (def.Icon, false) : DefaultLine(def));
            dockIcons[def] = icon;
        }
        return icon;
    }

    private static (Texture2D tex, bool line) DefaultLine(MainButtonDef def)
    {
        Texture2D line = ContentFinder<Texture2D>.Get("UI/JustDesktop/Tabs/" + def.defName, false);
        return line != null ? (line, true) : def.Icon != null ? (def.Icon, false) : (Generic, true);
    }

    // the icon in the title bar of a tab window, from the style in use
    public static (Texture2D tex, bool line) IconFor(MainButtonDef def)
    {
        return JustDesktopMod.Settings.style == BarStyle.Dock ? DockIconFor(def) : TaskbarIconFor(def);
    }
}
