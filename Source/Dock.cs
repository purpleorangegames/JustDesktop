using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// the other style: only icons, the name shows above the icon under the mouse
public static class Dock
{
    private const float Grow = 0.65f;

    private static float Scale => JustDesktopMod.Settings.size;
    private static float Size => 28f * Scale;
    private static float Pad => 5f * Scale;
    private static float Separator => 13f * Scale;
    private static float Edge => 8f * Scale;

    // null is a separator between groups
    private static readonly List<MainButtonDef> items = new();
    private static readonly List<float> widths = new();

    // where the dock sits without the magnification, for what goes next to it
    public static Rect Rest;

    private static float PanelX(float width)
    {
        return JustDesktopMod.Settings.align switch
        {
            BarAlign.Left => 6f,
            BarAlign.Right => UI.screenWidth - width - 6f,
            _ => (UI.screenWidth - width) / 2f
        };
    }

    public static void Draw(List<MainButtonDef> programs, List<MainButtonDef> tray)
    {
        Settings s = JustDesktopMod.Settings;
        items.Clear();
        if (MainButtonDefOf.Architect.Worker.Visible)
        {
            items.Add(MainButtonDefOf.Architect);
            if (s.separators)
            {
                items.Add(null);
            }
        }
        int lastGroup = -1;
        foreach (MainButtonDef def in programs)
        {
            if (!def.Worker.Visible)
            {
                continue;
            }
            int group = Taskbar.GroupOf(def);
            if (lastGroup >= 0 && group != lastGroup && s.separators)
            {
                items.Add(null);
            }
            lastGroup = group;
            items.Add(def);
        }
        bool first = true;
        foreach (MainButtonDef def in tray)
        {
            if (def.Worker.Visible)
            {
                if (first && s.separators)
                {
                    items.Add(null);
                }
                first = false;
                items.Add(def);
            }
        }

        // sizes at rest give the centers the magnification is measured from
        float rest = Edge * 2f;
        foreach (MainButtonDef def in items)
        {
            rest += def == null ? Separator : Size + Pad * 2f;
        }
        float height = Taskbar.Height - 2f;
        float bottom = UI.screenHeight - 2f;
        Rest = new Rect(PanelX(rest), bottom - height, rest, height);
        Vector2 mouse = UI.MousePositionOnUIInverted;
        float restX = PanelX(rest) + Edge;
        bool near = s.magnify && mouse.y > bottom - height - Size * Grow - 12f && mouse.x > restX - Size && mouse.x < restX + rest;
        widths.Clear();
        float total = Edge * 2f;
        foreach (MainButtonDef def in items)
        {
            float w;
            if (def == null)
            {
                w = Separator;
            }
            else
            {
                float scale = 1f;
                if (near)
                {
                    float d = Mathf.Abs(mouse.x - (restX + Size / 2f + Pad));
                    scale += Grow * Mathf.Cos(Mathf.Min(d / (Size * 2.5f), 1f) * Mathf.PI / 2f);
                }
                w = Size * scale + Pad * 2f;
            }
            restX += def == null ? Separator : Size + Pad * 2f;
            widths.Add(w);
            total += w;
        }

        Rect panel = new Rect(PanelX(total), bottom - height, total, height);
        if (s.back == BackMode.Bar)
        {
            GUI.color = s.backColor;
            Widgets.DrawAtlas(panel, Style.Round);
            GUI.color = Color.white;
        }

        float x = panel.x + Edge;
        for (int i = 0; i < items.Count; i++)
        {
            MainButtonDef def = items[i];
            float w = widths[i];
            if (def == null)
            {
                Widgets.DrawBoxSolid(new Rect(x + w / 2f - 0.5f, panel.y + panel.height * 0.24f, 1f, panel.height * 0.52f), Style.Divider);
                x += w;
                continue;
            }
            float size = w - Pad * 2f;
            Rect icon = new Rect(x + Pad, panel.yMax - 6f * Scale - size, size, size);
            Rect slot = new Rect(x, Mathf.Min(icon.y, panel.y), w, panel.yMax - Mathf.Min(icon.y, panel.y));
            bool over = Mouse.IsOver(slot);
            DrawIcon(def, icon, panel, over && !def.Worker.Disabled);
            if (over)
            {
                Taskbar.hoveredIcon = icon;
            }
            Taskbar.Click(slot, def);
            x += w;
        }
        GUI.color = Color.white;
    }

    private static void DrawIcon(MainButtonDef def, Rect icon, Rect panel, bool over)
    {
        MainButtonWorker worker = def.Worker;
        var (tex, line) = Style.DockIconFor(def);
        Style.ButtonBack(icon.ExpandedBy(Pad * 0.7f), over, rounded: true);
        Color color = line ? Style.Text : Color.white;
        if (worker.Disabled)
        {
            color.a = 0.35f;
        }
        GUI.color = color;
        GUI.DrawTexture(line ? icon.ContractedBy(icon.width * 0.12f) : icon, tex);

        float lineH = Mathf.Max(2f, Mathf.Round(2f * Scale));
        float y = panel.yMax - lineH - 1f;
        float progress = worker.ButtonBarPercent;
        if (progress > 0f)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(new Rect(icon.x + 2f, y, icon.width - 4f, lineH), BaseContent.WhiteTex);
            GUI.color = Style.Accent;
            GUI.DrawTexture(new Rect(icon.x + 2f, y, (icon.width - 4f) * Mathf.Clamp01(progress), lineH), BaseContent.WhiteTex);
        }
        bool open = Taskbar.IsOpen(def);
        if (JustDesktopMod.Settings.lines)
        {
            Taskbar.Underline(Taskbar.LineRect(icon.x + 2f, icon.width - 4f, panel.yMax - 1f, icon.y - lineH - 2f, lineH), open);
        }
        else if (open)
        {
            // open tabs get a dot like running programs
            float dot = Mathf.Round(4f * Scale);
            GUI.color = Style.Accent;
            GUI.DrawTexture(new Rect(icon.center.x - dot / 2f, panel.yMax - dot - 0.5f, dot, dot), Style.Circle);
        }
        GUI.color = Color.white;
    }
}
