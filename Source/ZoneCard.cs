using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JustDesktop;

// one selected zone on a card like a building's: a dot in the zone's color, its name and the rename button,
// what it is (a stockpile's priority, the plant a growing zone grows), bars for how full a stockpile is or how
// far along a growing zone's plants are, the game's text about it and its tabs (Storage)
public static class ZoneCard
{
    private static float Scale => Pane.Scale;
    private static float Width => Mathf.Round(320f * Scale);
    private const int MaxTextLines = 8;

    // the bars and the zone's middle are counted every so often, a big zone has thousands of cells
    private static Zone counted;
    private static int countedFrame = -9999;
    private static float used, planted, grown;
    private static Vector3 middle;
    private static readonly System.Collections.Generic.List<Pane.Bar> bars = new();

    private static string text;
    private static float textHeight;
    private static Vector2 textScroll;

    private static void Count(Zone z)
    {
        if (counted == z && Time.frameCount - countedFrame < 60)
        {
            return;
        }
        counted = z;
        countedFrame = Time.frameCount;
        Vector3 sum = Vector3.zero;
        int full = 0;
        foreach (IntVec3 c in z.Cells)
        {
            sum += c.ToVector3Shifted();
            if (z is Zone_Stockpile)
            {
                foreach (Thing t in z.Map.thingGrid.ThingsListAtFast(c))
                {
                    if (t.def.EverStorable(false))
                    {
                        full++;
                        break;
                    }
                }
            }
        }
        int cells = Mathf.Max(1, z.CellCount);
        middle = sum / cells;
        used = full / (float)cells;
        if (z is Zone_Growing g)
        {
            g.ContentsStatistics(out int plants, out _, out _, out float growth, out _);
            planted = plants / (float)cells;
            grown = growth;
        }

        bars.Clear();
        if (JustDesktopMod.Settings.paneBars)
        {
            if (z is Zone_Stockpile)
            {
                bars.Add(new Pane.Bar { Label = "Used", Value = used, ValueText = full + " / " + z.CellCount, Color = new Color(0.45f, 0.63f, 0.96f), Tip = "Cells with something stored in them" });
            }
            else if (z is Zone_Growing)
            {
                bars.Add(new Pane.Bar { Label = "Sown", Value = planted, Color = new Color(0.45f, 0.8f, 0.5f), Tip = "Cells with a plant in them" });
                bars.Add(new Pane.Bar { Label = "Grown", Value = grown, Color = new Color(0.93f, 0.77f, 0.33f), Tip = "How far along the plants are, on average" });
            }
        }

        text = null;
        try
        {
            text = z.GetInspectString()?.TrimEndNewlines();
        }
        catch (System.Exception e)
        {
            Log.ErrorOnce("[JustDesktop] couldn't get the text for " + z + ": " + e, 81723412);
        }
        textHeight = 0f;
        if (!text.NullOrEmpty())
        {
            GameFont old = Text.Font;
            Text.Font = Pane.SmallFont;
            textHeight = Mathf.Min(Mathf.Ceil(Text.CalcHeight(text, Width - Pane.Pad * 2f)), Mathf.Ceil(Text.LineHeight * MaxTextLines));
            Text.Font = old;
        }
    }

    private static string Kind(Zone z)
    {
        return z switch
        {
            Zone_Stockpile s => "Priority".Translate() + ": " + s.settings.Priority.Label().CapitalizeFirst(),
            Zone_Growing g when g.GetPlantDefToGrow() is { } plant => plant.LabelCap,
            _ => null
        };
    }

    public static Vector2 Size(Zone z)
    {
        Count(z);
        float h = Pane.Pad + 24f * Scale;
        if (!Kind(z).NullOrEmpty())
        {
            h += 16f * Scale;
        }
        h += 6f * Scale;
        if (bars.Count > 0)
        {
            h += bars.Count * (Pane.RowHeight + 2f) + 6f * Scale;
        }
        if (textHeight > 0f)
        {
            h += textHeight + 6f * Scale;
        }
        if (MainButtonDefOf.Inspect.TabWindow is IInspectPane pane && Pane.HasTabs(pane))
        {
            h += Pane.TabRow + 2f * Scale;
        }
        return new Vector2(Width, Mathf.Round(h + Pane.Pad));
    }

    public static Rect Place(Zone z)
    {
        Vector2 size = Size(z);
        return Pane.Place(size, middle);
    }

    public static void Draw(Rect inRect, Zone z, IInspectPane pane)
    {
        Count(z);
        pane.RecentHeight = inRect.height;
        Pane.CardBack(inRect);
        Rect inner = inRect.ContractedBy(Pane.Pad);
        Widgets.BeginGroup(inner);
        try
        {
            Rect area = inner.AtZero();
            float size = Mathf.Round(24f * Scale);
            Rect rename = new Rect(area.width - size, 0f, size, size);
            RenameUIUtility.DrawRenameButton(rename, z);

            float y = 0f;
            float dot = Mathf.Round(10f * Scale);
            GUI.color = new Color(z.color.r, z.color.g, z.color.b, 1f);
            GUI.DrawTexture(new Rect(0f, Mathf.Round(12f * Scale - dot / 2f), dot, dot), Style.Circle);
            GUI.color = Color.white;
            Text.Font = Pane.NameFont;
            Rect name = new Rect(dot + 8f * Scale, y, rename.x - dot - 14f * Scale, 24f * Scale);
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            Widgets.Label(name, z.label.CapitalizeFirst().Truncate(name.width));
            Text.WordWrap = true;
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonInvisible(name))
            {
                CameraJumper.TryJump(new GlobalTargetInfo(middle.ToIntVec3(), z.Map));
            }
            y += 24f * Scale;
            Text.Font = Pane.SmallFont;
            string kind = Kind(z);
            if (!kind.NullOrEmpty())
            {
                GUI.color = Style.Dim;
                Widgets.Label(new Rect(0f, y, area.width, 16f * Scale), kind);
                GUI.color = Color.white;
                y += 16f * Scale;
            }
            y += 6f * Scale;

            for (int i = 0; i < bars.Count; i++)
            {
                Pane.DrawBar(new Rect(0f, y + i * (Pane.RowHeight + 2f), area.width, Pane.RowHeight), bars[i], null);
            }
            if (bars.Count > 0)
            {
                y += bars.Count * (Pane.RowHeight + 2f) + 6f * Scale;
            }
            if (textHeight > 0f)
            {
                Text.Font = Pane.SmallFont;
                GUI.color = Style.Text;
                Widgets.LabelScrollable(new Rect(0f, y, area.width, textHeight), text, ref textScroll, dontConsumeScrollEventsIfNoScrollbar: true);
                GUI.color = Color.white;
            }
            if (Pane.HasTabs(pane))
            {
                Pane.DrawTabs(new Rect(0f, area.height - Pane.TabRow, area.width, Pane.TabRow), pane);
            }
        }
        finally
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
            Widgets.EndGroup();
        }
    }
}
