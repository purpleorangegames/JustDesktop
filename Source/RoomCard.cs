using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// the room a building is in, as a section above the card instead of a gizmo: its name, then impressiveness and the
// room's stats as bars, how far along each one's levels it is. Over it the room's edges show, like the game's gizmo
public static class RoomCard
{
    private static float Scale => Pane.Scale;
    private static float Row => Pane.RowHeight + 2f;

    private static readonly AccessTools.FieldRef<Gizmo_RoomStats, Building> building = AccessTools.FieldRefAccess<Gizmo_RoomStats, Building>("building");

    public static GizmoResult Draw(Gizmo_RoomStats gizmo, Rect card, out Rect rect)
    {
        rect = Rect.zero;
        Room room = Gizmo_RoomStats.GetRoomToShowStatsFor(building(gizmo));
        if (room == null)
        {
            return new GizmoResult(GizmoState.Clear);
        }
        int stats = 1;
        foreach (RoomStatDef def in DefDatabase<RoomStatDef>.AllDefsListForReading)
        {
            if (!def.isHidden && def != RoomStatDefOf.Impressiveness)
            {
                stats++;
            }
        }
        float h = Mathf.Round(Pane.Pad * 2f + 24f * Scale + 6f * Scale + stats * Row - 2f);
        rect = new Rect(card.x, card.y - 8f - h, card.width, h);
        Pane.CardBack(rect);
        Rect inner = rect.ContractedBy(Pane.Pad);
        Widgets.BeginGroup(inner);
        try
        {
            float y = 0f;
            Text.Font = Pane.NameFont;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            Rect name = new Rect(0f, y, inner.width, 24f * Scale);
            Widgets.Label(name, room.Role.PostProcessedLabelCap(room).Truncate(name.width));
            Text.WordWrap = true;
            Text.Anchor = TextAnchor.UpperLeft;
            y += 24f * Scale + 6f * Scale;
            Text.Font = Pane.SmallFont;
            DrawStat(new Rect(0f, y, inner.width, Pane.RowHeight), room, RoomStatDefOf.Impressiveness);
            y += Row;
            foreach (RoomStatDef def in DefDatabase<RoomStatDef>.AllDefsListForReading)
            {
                if (!def.isHidden && def != RoomStatDefOf.Impressiveness)
                {
                    DrawStat(new Rect(0f, y, inner.width, Pane.RowHeight), room, def);
                    y += Row;
                }
            }
        }
        finally
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Widgets.EndGroup();
        }
        GenUI.AbsorbClicksInRect(rect);
        return new GizmoResult(Mouse.IsOver(rect) ? GizmoState.Mouseover : GizmoState.Clear);
    }

    private static void DrawStat(Rect r, Room room, RoomStatDef def)
    {
        float score = room.GetStat(def);
        RoomStatScoreStage stage = def.GetScoreStage(score);
        string value = (stage?.label.CapitalizeFirst() ?? "") + "  ·  " + def.ScoreToString(score);
        Pane.DrawBar(r, new Pane.Bar { Label = def.LabelCap, Value = Level(def, score), ValueText = value, Color = ColorFor(def), Tip = def.LabelCap.AsTipTitle() + "\n\n" + value + (def.description.NullOrEmpty() ? "" : "\n\n" + def.description) }, null);
    }

    // where the score is among the stat's levels, each level an equal part of the bar
    private static float Level(RoomStatDef def, float score)
    {
        if (def.scoreStages.NullOrEmpty())
        {
            return 0.5f;
        }
        int count = def.scoreStages.Count;
        int i = def.GetScoreStageIndex(score);
        float part = 0.5f;
        if (i > 0 && i + 1 < count)
        {
            float low = def.scoreStages[i].minScore, high = def.scoreStages[i + 1].minScore;
            part = high > low ? Mathf.Clamp01((score - low) / (high - low)) : 0.5f;
        }
        else if (i == 0 && count > 1)
        {
            part = 1f;
        }
        return (i + part) / count;
    }

    private static Color ColorFor(RoomStatDef def) => def.defName switch
    {
        "Impressiveness" => new Color(0.93f, 0.77f, 0.33f),
        "Wealth" => new Color(0.85f, 0.68f, 0.4f),
        "Space" => new Color(0.45f, 0.63f, 0.96f),
        "Beauty" => new Color(0.9f, 0.55f, 0.75f),
        "Cleanliness" => new Color(0.45f, 0.8f, 0.72f),
        _ => new Color(0.55f, 0.68f, 0.8f)
    };
}
