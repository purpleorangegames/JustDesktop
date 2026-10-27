using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JustDesktop;

// the world map's selection pane (a caravan, settlement, site or tile) as a card like the map's: the name and the
// buttons on top, what the game says about it, the tabs as icons at the bottom. It goes where the map's card goes,
// the game's corner or centered above the dock, and the gizmos follow it
public static class WorldCard
{
    private static Settings S => JustDesktopMod.Settings;
    private static float Scale => S.paneSize;

    private static readonly Func<WorldInspectPane, string> tileText = AccessTools.MethodDelegate<Func<WorldInspectPane, string>>(AccessTools.PropertyGetter(typeof(WorldInspectPane), "TileInspectString"));

    private const int MaxLines = 9;
    private static float Width => Mathf.Round(320f * Scale);

    // one object or one tile selected on the world map, with the option on
    public static bool Shown
    {
        get
        {
            if (!S.paneOn || !Loading.Ready || Current.ProgramState != ProgramState.Playing || !WorldRendererUtility.WorldSelected)
            {
                return false;
            }
            WorldSelector sel = Find.WorldSelector;
            return sel.NumSelectedObjects == 1 || sel.NumSelectedObjects == 0 && sel.SelectedTile.Valid;
        }
    }

    private static string text;
    private static int textFrame = -1;
    private static float textHeight;
    private static Vector2 scroll;

    private static string Text(out float height)
    {
        if (textFrame != Time.frameCount)
        {
            textFrame = Time.frameCount;
            try
            {
                WorldObject obj = Find.WorldSelector.SingleSelectedObject;
                text = obj != null ? obj.GetInspectString() : tileText(Find.World.UI.inspectPane);
            }
            catch (Exception e)
            {
                text = null;
                Log.ErrorOnce("[JustDesktop] couldn't get the world selection's text: " + e, 81723412);
            }
            text = text?.TrimEndNewlines();
            textHeight = 0f;
            if (!text.NullOrEmpty())
            {
                GameFont old = Verse.Text.Font;
                Verse.Text.Font = Pane.SmallFont;
                textHeight = Mathf.Min(Mathf.Ceil(Verse.Text.CalcHeight(text, Width - Pane.Pad * 2f)), Mathf.Ceil(Verse.Text.LineHeight * MaxLines));
                Verse.Text.Font = old;
            }
        }
        height = textHeight;
        return text;
    }

    public static Vector2 Size
    {
        get
        {
            Text(out float textH);
            float h = Pane.Pad + 24f * Scale + 6f * Scale;
            if (textH > 0f)
            {
                h += textH + 6f * Scale;
            }
            if (Pane.HasTabs(Find.World.UI.inspectPane))
            {
                h += Pane.TabRow + 2f * Scale;
            }
            return new Vector2(Width, Mathf.Round(h + Pane.Pad));
        }
    }

    // the game's corner, or centered above the dock (next to the pawn means centered here)
    public static Rect Rect
    {
        get
        {
            Vector2 size = Size;
            float bottom = UI.screenHeight - Taskbar.Reserved;
            float x = S.panePlace == PanePlace.GamePlace ? 0f : Mathf.Round((UI.screenWidth - size.x) / 2f);
            return new Rect(x, bottom - size.y, size.x, size.y);
        }
    }

    public static void Draw(Rect inRect, IInspectPane pane)
    {
        pane.RecentHeight = inRect.height;
        Pane.CardBack(inRect);
        Rect inner = inRect.ContractedBy(Pane.Pad);
        Widgets.BeginGroup(inner);
        try
        {
            Rect area = inner.AtZero();
            float end = Buttons(area, pane);
            Verse.Text.Font = Scale >= 1.3f ? GameFont.Medium : GameFont.Small;
            Rect name = new Rect(0f, 0f, area.width - end - 6f, 24f * Scale);
            Verse.Text.Anchor = TextAnchor.MiddleLeft;
            Verse.Text.WordWrap = false;
            Widgets.Label(name, pane.GetLabel(name).Truncate(name.width));
            Verse.Text.WordWrap = true;
            Verse.Text.Anchor = TextAnchor.UpperLeft;
            if (Find.WorldSelector.SingleSelectedObject is { } obj && Widgets.ButtonInvisible(name))
            {
                CameraJumper.TryJump(obj);
            }
            float y = 24f * Scale + 6f * Scale;
            string info = Text(out float textH);
            if (textH > 0f)
            {
                Verse.Text.Font = Pane.SmallFont;
                GUI.color = Style.Text;
                Widgets.LabelScrollable(new Rect(0f, y, area.width, textH), info, ref scroll, dontConsumeScrollEventsIfNoScrollbar: true);
                GUI.color = Color.white;
            }
            if (Pane.HasTabs(pane))
            {
                Pane.DrawTabs(new Rect(0f, area.height - Pane.TabRow, area.width, Pane.TabRow), pane);
            }
        }
        finally
        {
            Verse.Text.Font = GameFont.Small;
            Verse.Text.Anchor = TextAnchor.UpperLeft;
            Verse.Text.WordWrap = true;
            GUI.color = Color.white;
            Widgets.EndGroup();
        }
    }

    // the info card and select the next one here, from the right
    private static float Buttons(Rect area, IInspectPane pane)
    {
        float size = Mathf.Round(24f * Scale);
        float x = area.width;
        Rect Next()
        {
            x -= size;
            Rect r = new Rect(x, 0f, size, size);
            x -= 4f * Scale;
            return r;
        }

        if (pane.ShouldShowSelectNextInCellButton)
        {
            Rect r = Next();
            if (Widgets.ButtonImage(r, TexButton.SelectOverlappingNext))
            {
                pane.SelectNextInCell();
            }
            TooltipHandler.TipRegionByKey(r, "SelectNextInSquareTip", KeyBindingDefOf.SelectNextInCell.MainKeyLabel);
        }
        WorldObject obj = Find.WorldSelector.SingleSelectedObject;
        if (obj != null)
        {
            Rect info = Next();
            Widgets.InfoCardButton(info.x, info.y, obj);
        }
        else if (Find.WorldSelector.SelectedTile.Valid && Find.WorldGrid[Find.WorldSelector.SelectedTile].PrimaryBiome is { } biome)
        {
            Rect info = Next();
            Widgets.InfoCardButton(info.x, info.y, biome);
        }
        return area.width - x;
    }

    // a caravan's mass, speed, food... on our card instead of the game's square background
    public static void InfoBack(Rect rect)
    {
        if (Gizmos.On)
        {
            Pane.CardBack(rect);
        }
        else
        {
            Widgets.DrawWindowBackground(rect);
        }
    }
}

[HarmonyPatch(typeof(Gizmo_CaravanInfo), nameof(Gizmo_CaravanInfo.GizmoOnGUI))]
public static class WorldCard_CaravanInfo
{
    public static System.Collections.Generic.IEnumerable<CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<CodeInstruction> instructions)
    {
        return Gizmos.Swap(instructions, AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawWindowBackground), new[] { typeof(Rect) }), AccessTools.Method(typeof(WorldCard), nameof(WorldCard.InfoBack)));
    }
}
