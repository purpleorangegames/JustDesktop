using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JustDesktop;

// the Social tab laid out to fit, as wide as the card: the ideoligion's certainty as a bar and the role with the
// game's role button, the relations as rows (relation, name, where they stand, both opinions), the romance button,
// and the interactions log as compact lines. Babies keep the game's, which shows how ideoligions reach them
public static class SocialPanel
{
    private static float Scale => Pane.Scale;
    private static float Width => Mathf.Round(400f * Scale);
    private static float Line => Mathf.Round(20f * Scale);
    private static float Heading => Mathf.Round(18f * Scale);
    private static float Section => Mathf.Round(8f * Scale);
    private static float Control => Mathf.Round(22f * Scale);
    private const int MaxRelationRows = 12;
    private const int MaxLogEntries = 12;
    private static float MaxLogHeight => Mathf.Round(150f * Scale);

    // the game keeps the relations in its own cached list, of a type it keeps to itself
    private static readonly Action<Pawn> checkRecache = AccessTools.MethodDelegate<Action<Pawn>>(AccessTools.Method(typeof(SocialCardUtility), "CheckRecache"));
    private static readonly FieldInfo cachedEntries = AccessTools.Field(typeof(SocialCardUtility), "cachedEntries");
    private static readonly Type entryType = AccessTools.Inner(typeof(SocialCardUtility), "CachedSocialTabEntry");
    private static readonly FieldInfo otherPawn = AccessTools.Field(entryType, "otherPawn");
    private static readonly FieldInfo opinionOfOther = AccessTools.Field(entryType, "opinionOfOtherPawn");
    private static readonly FieldInfo opinionOfMe = AccessTools.Field(entryType, "opinionOfMe");
    private static readonly FieldInfo pregnancyApproach = AccessTools.Field(entryType, "pregnancyApproach");
    private static readonly FastInvokeHandler relationsString = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(typeof(SocialCardUtility), "GetRelationsString"));
    private static readonly FastInvokeHandler rowTooltip = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(typeof(SocialCardUtility), "GetPawnRowTooltip"));
    private static readonly FastInvokeHandler drawPregnancy = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(typeof(SocialCardUtility), "DrawPregnancyApproach"));
    private static readonly Func<Pawn, bool> canRomance = AccessTools.MethodDelegate<Func<Pawn, bool>>(AccessTools.Method(typeof(SocialCardUtility), "CanDrawTryRomance"));
    private static readonly Action<Rect, Pawn> drawRomance = AccessTools.MethodDelegate<Action<Rect, Pawn>>(AccessTools.Method(typeof(SocialCardUtility), "DrawTryRomance"));

    private static Vector2 relationScroll, logScroll;
    private static readonly List<LogEntry> log = new();

    public static bool Shows(Pawn p) => !p.DevelopmentalStage.Baby();

    private static bool Ideo(Pawn p) => ModsConfig.IdeologyActive && !p.Dead && p.Ideo != null;

    private static IList Entries(Pawn p)
    {
        checkRecache(p);
        return (IList)cachedEntries.GetValue(null);
    }

    private static float LogTextWidth(float width) => width - Mathf.Round(20f * Scale) - 16f;

    private static void CollectLog(Pawn p)
    {
        log.Clear();
        foreach (LogEntry entry in Find.PlayLog.AllEntries)
        {
            if (entry.Concerns(p))
            {
                log.Add(entry);
                if (log.Count >= MaxLogEntries)
                {
                    break;
                }
            }
        }
    }

    private static float LogEntryHeight(LogEntry entry, Pawn p, float width) => Mathf.Max(Line, Mathf.Ceil(Text.CalcHeight(entry.ToGameStringFromPOV(p), LogTextWidth(width))));

    private static float LogHeight(Pawn p, float width)
    {
        float h = 0f;
        foreach (LogEntry entry in log)
        {
            h += LogEntryHeight(entry, p, width);
        }
        return Mathf.Min(Mathf.Max(h, Line), MaxLogHeight);
    }

    public static Vector2 Size(Pawn p)
    {
        GameFont font = Text.Font;
        Text.Font = Pane.SmallFont;
        float inner = Width - Pane.Pad * 2f;
        float h = Pane.Pad + 24f * Scale + Section;
        if (Ideo(p))
        {
            if (!p.Ideo.classicMode)
            {
                h += Control + 4f * Scale;
            }
            h += Control + Section;
        }
        h += Heading + Mathf.Max(1, Mathf.Min(Entries(p).Count, MaxRelationRows)) * Line + Section;
        CollectLog(p);
        h += Heading + LogHeight(p, inner);
        Text.Font = font;
        return new Vector2(Width, Mathf.Round(h + Pane.Pad));
    }

    public static void Draw(Rect all, Pawn p)
    {
        Rect inner = all.ContractedBy(Pane.Pad);
        Widgets.BeginGroup(inner);
        try
        {
            Rect area = inner.AtZero();
            Text.Font = Pane.NameFont;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(0f, 0f, area.width / 2f, 24f * Scale), "TabSocial".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            if (canRomance(p))
            {
                // the game's button, by the title, clear of our close button
                drawRomance(new Rect(area.width - 24f - Mathf.Round(115f * Scale), 0f, Mathf.Round(115f * Scale), Control), p);
            }
            Text.Font = Pane.SmallFont;
            float y = 24f * Scale + Section;
            if (Ideo(p))
            {
                y = Ideoligion(y, area.width, p);
            }
            y = Relations(y, area.width, p);
            CollectLog(p);
            Interactions(new Rect(0f, y, area.width, area.height - y), p);
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

    private static float DrawHeading(float y, float width, string label)
    {
        GUI.color = Style.Dim;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(0f, y, width, Heading), label);
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        return y + Heading;
    }

    // ---- ideoligion: certainty and role ----

    private static float Ideoligion(float y, float width, Pawn p)
    {
        Ideo ideo = p.Ideo;
        if (!ideo.classicMode)
        {
            Rect row = new Rect(0f, y, width, Control);
            float icon = Control;
            ideo.DrawIcon(new Rect(row.x, row.y, icon, icon));
            Rect bar = new Rect(row.x + icon + 6f * Scale, Mathf.Round(row.center.y - Pane.RowHeight / 2f), row.width - icon - 6f * Scale, Pane.RowHeight);
            Pane.DrawBar(bar, new Pane.Bar { Label = ideo.name, Value = p.ideo.Certainty, ValueText = p.ideo.Certainty.ToStringPercent(), Color = ideo.Color, Tip = "" }, null);
            if (Mouse.IsOver(row))
            {
                TooltipHandler.TipRegion(row, () => CertaintyTip(p), 10218219);
            }
            if (Widgets.ButtonInvisible(row))
            {
                IdeoUIUtility.OpenIdeoInfo(ideo);
            }
            y += Control + 4f * Scale;
        }
        Precept_Role role = ideo.GetRole(p);
        Rect roleRow = new Rect(0f, y, width, Control);
        float x = 0f;
        if (role != null)
        {
            GUI.color = ideo.Color;
            Widgets.DrawTextureFitted(new Rect(0f, roleRow.y, Control, Control), role.Icon, 1f);
            GUI.color = Color.white;
            x = Control + 6f * Scale;
        }
        // the game's button: it goes from 150 to 30 before the right of what it's given, so that's 30 past ours
        float button = 120f;
        Rect label = new Rect(x, roleRow.y, width - x - button - 8f, roleRow.height);
        GUI.color = role != null ? Style.Text : Style.Dim;
        Text.Anchor = TextAnchor.MiddleLeft;
        Text.WordWrap = false;
        string roleLabel = role != null ? role.LabelForPawn(p) : "NoRoleAssigned".Translate().ToString();
        Widgets.Label(label, roleLabel.Truncate(label.width));
        Text.WordWrap = true;
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        if (Mouse.IsOver(label))
        {
            Widgets.DrawHighlight(label);
            string desc = "RoleDesc".Translate().Resolve();
            if (role != null)
            {
                desc += "\n\n" + role.LabelForPawn(p) + ": " + role.GetTip();
            }
            TooltipHandler.TipRegion(label, new TipSignal(() => desc, p.thingIDNumber * 39));
        }
        Text.Font = GameFont.Small;
        SocialCardUtility.DrawPawnRoleSelection(p, new Rect(0f, Mathf.Round(roleRow.center.y - 14f), width + 30f, 28f));
        Text.Font = Pane.SmallFont;
        return y + Control + Section;
    }

    // the game's tooltip for certainty: how sure they are, how fast it changes and why, and what mood does to it
    private static string CertaintyTip(Pawn pawn)
    {
        string change = pawn.ideo.CertaintyChangePerDay.ToStringPercent();
        if (pawn.ideo.CertaintyChangePerDay >= 0f)
        {
            change = "+" + change;
        }
        TaggedString tip = "CertaintyInIdeo".Translate(pawn.Named("PAWN"), pawn.Ideo.Named("IDEO")) + ": " + pawn.ideo.Certainty.ToStringPercent() + "\n\n" + "CertaintyChangePerDay".Translate() + ": " + change + "\n\n";
        float loss = pawn.GetStatValue(StatDefOf.CertaintyLossFactor);
        if (loss != 1f)
        {
            tip += StatDefOf.CertaintyLossFactor.LabelCap + ": " + loss.ToStringPercent();
            foreach (Trait trait in pawn.story.traits.allTraits)
            {
                float factor = trait.Suppressed ? 1f : trait.MultiplierOfStat(StatDefOf.CertaintyLossFactor);
                if (factor != 1f)
                {
                    tip += "\n -  " + "AbilityIdeoConvertBreakdownTrait".Translate(trait.LabelCap.Named("TRAIT")) + ": x" + factor.ToStringPercent();
                }
            }
            foreach (Precept precept in pawn.Ideo.PreceptsListForReading)
            {
                float factor = precept.def.statFactors?.GetStatFactorFromList(StatDefOf.CertaintyLossFactor) ?? 1f;
                if (factor != 1f)
                {
                    tip += "\n -  " + "AbilityIdeoConvertBreakdownPercept".Translate(precept.LabelCap.Named("PRECEPT")) + ": x" + factor.ToStringPercent();
                }
            }
            tip += "\n\n";
        }
        string reduction = ConversionUtility.GetCertaintyReductionFactorsDescription(pawn).Resolve();
        if (!reduction.NullOrEmpty())
        {
            tip += reduction + "\n\n";
        }
        if (pawn.story?.traits != null)
        {
            List<string> agree = new(), disagree = new();
            foreach (Trait trait in pawn.story.traits.allTraits.Where(t => !t.Suppressed))
            {
                foreach (MemeDef meme in trait.CurrentData.GetAffectedMemes(trait.def, true).Where(pawn.Ideo.HasMeme))
                {
                    agree.Add(trait.LabelCap + "/" + meme.LabelCap);
                }
                foreach (MemeDef meme in trait.CurrentData.GetAffectedMemes(trait.def, false).Where(pawn.Ideo.HasMeme))
                {
                    disagree.Add(trait.LabelCap + "/" + meme.LabelCap);
                }
            }
            string memes = "";
            if (agree.Any())
            {
                memes += "ConversionWeakerDueToTraitAgreements".Translate() + ":\n" + agree.ToLineList("  - ");
            }
            if (disagree.Any())
            {
                memes += (memes.NullOrEmpty() ? "" : "\n\n") + "ConversionStrongerDueToTraitAgreements".Translate() + ":\n" + disagree.ToLineList("  - ");
            }
            if (!memes.NullOrEmpty())
            {
                tip += memes + "\n\n";
            }
        }
        string mood = "MoodChangeRate".Translate() + ": ";
        foreach (CurvePoint point in ConversionTuning.CertaintyPerDayByMoodCurve.Points)
        {
            string rate = point.y.ToStringPercent();
            if (point.y >= 0f)
            {
                rate = "+" + rate;
            }
            mood += "\n -  " + "Mood".Translate() + " " + point.x.ToStringPercent() + ": " + "PerDay".Translate(rate);
        }
        tip += mood.Colorize(Color.grey);
        return tip.Resolve();
    }

    // ---- relations ----

    private static readonly Color Good = new(0.5f, 0.85f, 0.5f);
    private static readonly Color Bad = new(0.92f, 0.45f, 0.45f);

    // the game's colors for an opinion: gray when it's small, green or red otherwise
    private static Color OpinionColor(int opinion) => Mathf.Abs(opinion) < 10 ? Style.Dim : opinion < 0 ? Bad : Good;

    private static float Relations(float y, float width, Pawn p)
    {
        IList entries = Entries(p);
        y = DrawHeading(y, width, "Relations");
        Rect list = new Rect(0f, y, width, Mathf.Max(1, Mathf.Min(entries.Count, MaxRelationRows)) * Line);
        if (entries.Count == 0)
        {
            GUI.color = ColoredText.SubtleGrayColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(5f * Scale, y, width, Line), "None".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            return list.yMax + Section;
        }
        bool scrolls = entries.Count > MaxRelationRows;
        Rect view = new Rect(0f, 0f, list.width - (scrolls ? 16f : 0f), entries.Count * Line);
        Widgets.BeginScrollView(list, ref relationScroll, view, scrolls);
        for (int i = 0; i < entries.Count; i++)
        {
            float rowY = i * Line;
            // only the rows that show
            if (rowY + Line < relationScroll.y || rowY > relationScroll.y + list.height)
            {
                continue;
            }
            RelationRow(new Rect(0f, rowY, view.width, Line), entries[i], p);
        }
        Widgets.EndScrollView();
        return list.yMax + Section;
    }

    // relation, name, where they stand, my opinion of them and theirs of me (dimmer), and the pregnancy approach
    private static void RelationRow(Rect row, object entry, Pawn p)
    {
        Pawn other = (Pawn)otherPawn.GetValue(entry);
        bool pregnancy = ((PregnancyApproach?)pregnancyApproach.GetValue(entry)).HasValue;
        Rect hover = row;
        if (pregnancy)
        {
            hover.xMax -= Line + 4f * Scale;
        }
        if (Mouse.IsOver(hover))
        {
            GUI.color = new Color(1f, 1f, 1f, 0.06f);
            Widgets.DrawAtlas(row, ModernWidgets.Fill);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(hover, () => (string)rowTooltip(null, entry, p), other.thingIDNumber * 13 + p.thingIDNumber);
        }
        if (Widgets.ButtonInvisible(hover))
        {
            if (other.Dead)
            {
                Messages.Message("MessageCantSelectDeadPawn".Translate(other.LabelShort, other).CapitalizeFirst(), MessageTypeDefOf.RejectInput, false);
            }
            else if (other.SpawnedOrAnyParentSpawned || other.IsCaravanMember())
            {
                CameraJumper.TryJumpAndSelect(other);
            }
            else
            {
                Messages.Message("MessageCantSelectOffMapPawn".Translate(other.LabelShort, other).CapitalizeFirst(), MessageTypeDefOf.RejectInput, false);
            }
        }

        bool opinions = other.RaceProps.Humanlike && p.RaceProps.Humanlike;
        float number = Mathf.Round(30f * Scale);
        float right = row.xMax - (pregnancy ? Line + 4f * Scale : 0f);
        float relationWidth = Mathf.Round(row.width * 0.24f), nameWidth = Mathf.Round(row.width * 0.32f);
        float x = row.x + 5f * Scale;
        Text.Anchor = TextAnchor.MiddleLeft;
        Text.WordWrap = false;
        GUI.color = Style.Dim;
        string relation = (string)relationsString(null, entry, p);
        Widgets.Label(new Rect(x, row.y, relationWidth - 4f * Scale, row.height), relation.Truncate(relationWidth - 4f * Scale));
        x += relationWidth;
        GUI.color = Style.Text;
        string name = other.Name != null ? other.Name.ToStringShort : other.LabelCapNoCount;
        Widgets.Label(new Rect(x, row.y, nameWidth - 4f * Scale, row.height), name.Truncate(nameWidth - 4f * Scale));
        x += nameWidth;
        float numbers = opinions ? number * 2f : 0f;
        string situation = SocialCardUtility.GetPawnSituationLabel(other, p);
        if (!situation.NullOrEmpty())
        {
            GUI.color = Style.Dim;
            float w = right - numbers - x - 4f * Scale;
            Widgets.Label(new Rect(x, row.y, w, row.height), situation.Truncate(w));
        }
        Text.Anchor = TextAnchor.MiddleRight;
        if (opinions)
        {
            int mine = (int)opinionOfOther.GetValue(entry), theirs = (int)opinionOfMe.GetValue(entry);
            GUI.color = OpinionColor(mine);
            Widgets.Label(new Rect(right - numbers, row.y, number, row.height), mine.ToStringWithSign());
            Color c = OpinionColor(theirs);
            GUI.color = new Color(c.r, c.g, c.b, 0.5f);
            Widgets.Label(new Rect(right - number, row.y, number - 2f * Scale, row.height), "(" + theirs.ToStringWithSign() + ")");
        }
        Text.Anchor = TextAnchor.UpperLeft;
        Text.WordWrap = true;
        GUI.color = Color.white;
        if (pregnancy)
        {
            drawPregnancy(null, entry, new Rect(row.xMax - Line, row.y, Line, Line), p);
            GUI.color = Color.white;
        }
    }

    // ---- interactions ----

    private static void Interactions(Rect rect, Pawn p)
    {
        float y = DrawHeading(rect.y, rect.width, "Interactions");
        Rect list = new Rect(rect.x, y, rect.width, LogHeight(p, rect.width));
        if (log.Count == 0)
        {
            GUI.color = ColoredText.SubtleGrayColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(list.x + 5f * Scale, list.y, list.width, Line), "None".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            return;
        }
        float total = log.Sum(e => LogEntryHeight(e, p, rect.width));
        bool scrolls = total > list.height + 0.5f;
        Rect view = new Rect(0f, 0f, list.width - (scrolls ? 16f : 0f), total);
        Widgets.BeginScrollView(list, ref logScroll, view, scrolls);
        float icon = Mathf.Round(16f * Scale), cy = 0f;
        for (int i = 0; i < log.Count; i++)
        {
            LogEntry entry = log[i];
            float h = LogEntryHeight(entry, p, rect.width);
            Rect row = new Rect(0f, cy, view.width, h);
            Texture2D tex = entry.IconFromPOV(p);
            if (tex != null)
            {
                GUI.color = entry.IconColorFromPOV(p) ?? Color.white;
                GUI.DrawTexture(new Rect(row.x + 2f * Scale, row.y + Mathf.Round((Line - icon) / 2f), icon, icon), tex);
            }
            Rect text = new Rect(row.x + icon + 6f * Scale, row.y, LogTextWidth(rect.width), h);
            if (Mouse.IsOver(text))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.06f);
                Widgets.DrawAtlas(row, ModernWidgets.Fill);
                LogEntry local = entry;
                TooltipHandler.TipRegion(text, () => local.GetTipString(), 613261 + i * 611);
            }
            // older ones fainter, like the game's
            GUI.color = entry.Age > 7500 ? Style.Dim : Style.Text;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(text, entry.ToGameStringFromPOV(p));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (Widgets.ButtonInvisible(text, entry.CanBeClickedFromPOV(p)))
            {
                entry.ClickedFromPOV(p);
            }
            cy += h;
        }
        Widgets.EndScrollView();
    }
}
