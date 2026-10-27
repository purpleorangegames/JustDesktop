using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// the Factions window in the modern windows: each faction a row with its icon, name, kind and leader; goodwill as a
// meter from -100 to 100 (the ally and hostile lines marked, where it drifts to as a small mark) with the relation
// as a colored pill; ideoligions and who they're enemies with in their own columns. Tooltips and clicks are the game's
public static class FactionsWindow
{
    private const float RowHeight = 64f;
    private const float IdentityWidth = 330f;
    private const float GoodwillWidth = 300f;
    private const float IdeoWidth = 96f;

    private static readonly AccessTools.FieldRef<bool> showAll = AccessTools.StaticFieldRefAccess<bool>(AccessTools.Field(typeof(FactionUIUtility), "showAll"));
    private static readonly Func<Faction, TaggedString> recentEvents = AccessTools.MethodDelegate<Func<Faction, TaggedString>>(AccessTools.Method(typeof(FactionUIUtility), "GetRecentEvents"));
    private static readonly Func<Faction, TaggedString> ongoingEvents = AccessTools.MethodDelegate<Func<Faction, TaggedString>>(AccessTools.Method(typeof(FactionUIUtility), "GetOngoingEvents"));
    private static readonly Func<Faction, TaggedString> naturalExplanation = AccessTools.MethodDelegate<Func<Faction, TaggedString>>(AccessTools.Method(typeof(FactionUIUtility), "GetNaturalGoodwillExplanation"));
    private static readonly Func<int, FactionRelationKind> relationFor = AccessTools.MethodDelegate<Func<int, FactionRelationKind>>(AccessTools.Method(typeof(FactionUIUtility), "GetRelationKindForGoodwill"));

    private static readonly List<Faction> factions = new();

    public static void Draw(Rect fillRect, ref Vector2 scroll, ref float viewHeight, Faction scrollTo)
    {
        Rect rect = fillRect.AtZero();
        Widgets.BeginGroup(rect);
        Text.Font = GameFont.Small;
        if (Prefs.DevMode)
        {
            Widgets.CheckboxLabeled(new Rect(rect.width - 120f, 0f, 120f, 24f), "DEV: Show all", ref showAll());
        }
        else
        {
            showAll() = false;
        }
        factions.Clear();
        foreach (Faction f in Find.FactionManager.AllFactionsInViewOrder)
        {
            if ((!f.IsPlayer && !f.Hidden) || showAll())
            {
                factions.Add(f);
            }
        }
        if (factions.Count == 0)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, "NoFactions".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.EndGroup();
            return;
        }
        // column titles
        float top = 30f;
        Text.Font = GameFont.Tiny;
        GUI.color = Style.Dim;
        Widgets.Label(new Rect(IdentityWidth + 8f, top, GoodwillWidth, 20f), "Goodwill".Translate().CapitalizeFirst());
        if (ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode)
        {
            Widgets.Label(new Rect(IdentityWidth + GoodwillWidth + 24f, top, IdeoWidth, 20f), "Ideoligions");
        }
        ColumnToggle(new Rect(IdentityWidth + GoodwillWidth + 24f + IdeoWidth + 8f, top - 2f, 200f, 20f));
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        Rect outRect = new Rect(0f, top + 20f, rect.width, rect.height - top - 20f);
        Rect view = new Rect(0f, 0f, rect.width - 16f, viewHeight);
        Widgets.BeginScrollView(outRect, ref scroll, view);
        float y = 0f;
        for (int i = 0; i < factions.Count; i++)
        {
            if (factions[i] == scrollTo)
            {
                scroll.y = y;
            }
            Row(new Rect(0f, y, view.width, RowHeight), factions[i], i);
            y += RowHeight + 4f;
        }
        if (Event.current.type == EventType.Layout)
        {
            viewHeight = y;
        }
        Widgets.EndScrollView();
        Widgets.EndGroup();
    }

    private static void Row(Rect row, Faction f, int index)
    {
        bool defeated = f.defeated;
        GUI.color = new Color(1f, 1f, 1f, index % 2 == 0 ? 0.045f : 0.025f);
        Widgets.DrawAtlas(row, ModernWidgets.Fill);
        GUI.color = Color.white;

        // who they are: icon, name, kind and leader; click for the info card
        Rect identity = new Rect(row.x, row.y, IdentityWidth, row.height);
        if (Mouse.IsOver(identity))
        {
            GUI.color = new Color(1f, 1f, 1f, 0.05f);
            Widgets.DrawAtlas(identity, ModernWidgets.Fill);
            TooltipHandler.TipRegion(identity, new TipSignal(() => f.Name.Colorize(ColoredText.TipSectionTitleColor) + "\n" + f.def.LabelCap.Resolve() + "\n\n" + f.def.Description, f.loadID ^ 0x738AC053));
        }
        if (Widgets.ButtonInvisible(identity, false))
        {
            Find.WindowStack.Add(new Dialog_InfoCard(f));
        }
        float icon = 36f;
        GUI.color = defeated ? new Color(f.Color.r, f.Color.g, f.Color.b, 0.4f) : f.Color;
        GUI.DrawTexture(new Rect(row.x + 14f, Mathf.Round(row.center.y - icon / 2f), icon, icon), f.def.FactionIcon);
        float x = row.x + 14f + icon + 12f;
        Text.Anchor = TextAnchor.LowerLeft;
        Text.WordWrap = false;
        GUI.color = defeated ? Style.Dim : Color.white;
        Rect name = new Rect(x, row.y + 4f, IdentityWidth - x - 40f, row.height / 2f - 2f);
        Widgets.Label(name, f.Name.CapitalizeFirst().Truncate(name.width));
        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Style.Dim;
        string about = f.def.LabelCap + (f.leader != null ? "  ·  " + f.LeaderTitle.CapitalizeFirst() + ": " + f.leader.Name.ToStringFull : "");
        Rect aboutRect = new Rect(x, row.center.y + 2f, IdentityWidth - x - 40f, 20f);
        Widgets.Label(aboutRect, about.Truncate(aboutRect.width));
        Text.Font = GameFont.Small;
        Text.WordWrap = true;
        GUI.color = Color.white;
        Widgets.InfoCardButtonCentered(new Rect(row.x + IdentityWidth - 36f, row.y, 32f, row.height), f);

        Goodwill(new Rect(row.x + IdentityWidth + 8f, row.y, GoodwillWidth, row.height), f);

        float ideoX = row.x + IdentityWidth + GoodwillWidth + 24f;
        if (ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode && f.ideos != null)
        {
            Ideos(new Rect(ideoX, row.y, IdeoWidth, row.height), f);
        }
        Enemies(new Rect(ideoX + IdeoWidth + 8f, row.y, row.xMax - ideoX - IdeoWidth - 16f, row.height), f);
    }

    // a pill with the relation and the goodwill over a meter from -100 to 100
    private static void Goodwill(Rect rect, Faction f)
    {
        if (f.IsPlayer)
        {
            return;
        }
        FactionRelationKind kind = f.PlayerRelationKind;
        Color color = kind.GetColor();
        bool meter = f.HasGoodwill && !f.def.permanentEnemy;
        string label = kind.GetLabelCap() + (meter ? "  " + f.PlayerGoodwill.ToStringWithSign() : f.def.permanentEnemy ? "  ·  " + "Permanent".Translate().ToString().ToLower() : "");
        if (f.defeated)
        {
            label = "Defeated" + "  ·  " + label;
            color = Style.Dim;
        }
        Text.Font = GameFont.Small;
        float w = Text.CalcSize(label).x + 20f;
        Rect pill = new Rect(rect.x, rect.y + 8f, w, 24f);
        GUI.color = new Color(color.r, color.g, color.b, 0.22f);
        Widgets.DrawAtlas(pill, ModernWidgets.Fill);
        GUI.color = color;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(pill, label);
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;

        if (meter)
        {
            Rect bar = new Rect(rect.x, rect.y + 40f, rect.width, 10f);
            float X(int goodwill) => Mathf.Round(bar.x + bar.width * Mathf.InverseLerp(-100f, 100f, goodwill));
            GUI.color = new Color(1f, 1f, 1f, 0.07f);
            Widgets.DrawAtlas(bar, ModernWidgets.Fill);
            float zero = X(0), at = X(f.PlayerGoodwill);
            GUI.color = new Color(color.r, color.g, color.b, 0.85f);
            if (at != zero)
            {
                Widgets.DrawBoxSolid(new Rect(Mathf.Min(zero, at), bar.y + 1f, Mathf.Abs(at - zero), bar.height - 2f), GUI.color);
            }
            // zero, and where it turns hostile and ally
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            GUI.DrawTexture(new Rect(zero, bar.y - 2f, 1f, bar.height + 4f), BaseContent.WhiteTex);
            GUI.color = new Color(1f, 1f, 1f, 0.18f);
            GUI.DrawTexture(new Rect(X(-75), bar.y, 1f, bar.height), BaseContent.WhiteTex);
            GUI.color = new Color(1f, 1f, 1f, 0.18f);
            GUI.DrawTexture(new Rect(X(75), bar.y, 1f, bar.height), BaseContent.WhiteTex);
            // where it drifts to on its own
            int natural = f.NaturalGoodwill;
            Color naturalColor = relationFor(natural).GetColor();
            float n = X(natural);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(n - 5f, bar.y - 4f, 10f, 10f), Style.Circle);
            GUI.color = naturalColor;
            GUI.DrawTexture(new Rect(n - 3.5f, bar.y - 2.5f, 7f, 7f), Style.Circle);
            GUI.color = Color.white;
        }

        Rect hover = new Rect(rect.x, rect.y, rect.width, rect.height);
        if (Mouse.IsOver(hover))
        {
            TooltipHandler.TipRegion(hover, new TipSignal(() => Tip(f), f.loadID * 13 + 5));
        }
    }

    // the game's goodwill tooltip and its natural goodwill one, together
    private static string Tip(Faction f)
    {
        TaggedString tip = "";
        if (f.def.permanentEnemy)
        {
            tip = "CurrentGoodwillTip_PermanentEnemy".Translate();
        }
        else if (f.HasGoodwill)
        {
            tip = "Goodwill".Translate().Colorize(ColoredText.TipSectionTitleColor) + ": " + (f.PlayerGoodwill.ToStringWithSign() + ", " + f.PlayerRelationKind.GetLabel()).Colorize(f.PlayerRelationKind.GetColor());
            TaggedString ongoing = ongoingEvents(f);
            if (!ongoing.NullOrEmpty())
            {
                tip += "\n\n" + "OngoingEvents".Translate().Colorize(ColoredText.TipSectionTitleColor) + ":\n" + ongoing;
            }
            TaggedString recent = recentEvents(f);
            if (!recent.NullOrEmpty())
            {
                tip += "\n\n" + "RecentEvents".Translate().Colorize(ColoredText.TipSectionTitleColor) + ":\n" + recent;
            }
            string kind = f.PlayerRelationKind switch
            {
                FactionRelationKind.Ally => "CurrentGoodwillTip_Ally".Translate(0.ToString("F0")),
                FactionRelationKind.Neutral => "CurrentGoodwillTip_Neutral".Translate((-75).ToString("F0"), 75.ToString("F0")),
                FactionRelationKind.Hostile => "CurrentGoodwillTip_Hostile".Translate(0.ToString("F0")),
                _ => ""
            };
            tip += "\n\n" + kind.Colorize(ColoredText.SubtleGrayColor);
            int natural = f.NaturalGoodwill;
            int low = Mathf.Clamp(natural - 50, -100, 100), high = Mathf.Clamp(natural + 50, -100, 100);
            tip += "\n\n" + "NaturalGoodwill".Translate().Colorize(ColoredText.TipSectionTitleColor) + ": " + natural.ToStringWithSign().Colorize(relationFor(natural).GetColor());
            tip += "\n" + "NaturalGoodwillRange".Translate().Colorize(ColoredText.TipSectionTitleColor) + ": " + low.ToString().Colorize(relationFor(low).GetColor()) + " " + "RangeTo".Translate() + " " + high.ToString().Colorize(relationFor(high).GetColor());
            TaggedString why = naturalExplanation(f);
            if (!why.NullOrEmpty())
            {
                tip += "\n\n" + "AffectedBy".Translate().Colorize(ColoredText.TipSectionTitleColor) + "\n" + why;
            }
            tip += "\n\n" + "NaturalGoodwillDescription".Translate(1.25f.ToStringPercent()).Colorize(ColoredText.SubtleGrayColor);
        }
        return tip.Resolve();
    }

    // the main ideoligion bigger, the others small after it
    private static void Ideos(Rect rect, Faction f)
    {
        float x = rect.x;
        if (f.ideos.PrimaryIdeo is { } primary)
        {
            Rect r = new Rect(x, Mathf.Round(rect.center.y - 16f), 32f, 32f);
            IdeoUIUtility.DoIdeoIcon(r, primary, true, () => IdeoUIUtility.OpenIdeoInfo(primary));
            x += 38f;
        }
        List<Ideo> minor = f.ideos.IdeosMinorListForReading;
        float y = rect.center.y - 20f;
        for (int i = 0; i < minor.Count; i++)
        {
            if (x + 18f > rect.xMax)
            {
                x = rect.x + 38f;
                y += 22f;
            }
            if (y + 18f > rect.yMax)
            {
                break;
            }
            Ideo local = minor[i];
            IdeoUIUtility.DoIdeoIcon(new Rect(x, y, 18f, 18f), local, true, () => IdeoUIUtility.OpenIdeoInfo(local));
            x += 22f;
        }
    }

    // the last column shows who they're hostile to, or who they aren't; the title is the switch, until the game closes
    private static bool notEnemies = true;

    private static void ColumnToggle(Rect rect)
    {
        string label = notEnemies ? "Not enemy of" : "EnemyOf".Translate().ToString().TrimEnd(':');
        Text.Font = GameFont.Tiny;
        Rect pill = new Rect(rect.x - 6f, rect.y, Text.CalcSize(label).x + 12f, rect.height);
        bool over = Mouse.IsOver(pill);
        GUI.color = over ? new Color(1f, 1f, 1f, 0.14f) : new Color(1f, 1f, 1f, 0.07f);
        Widgets.DrawAtlas(pill, ModernWidgets.Fill);
        GUI.color = over ? Color.white : Style.Text;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(pill, label);
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Style.Dim;
        TooltipHandler.TipRegion(pill, notEnemies ? "Showing the factions each one isn't hostile to. Click to show its enemies" : "Showing the factions each one is hostile to. Click to show the ones it isn't");
        if (Widgets.ButtonInvisible(pill))
        {
            notEnemies = !notEnemies;
            Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_Tiny);
        }
    }

    // the factions they're hostile to (or not, with the switch), as their icons, wrapping when there are many
    private static void Enemies(Rect rect, Faction f)
    {
        Faction[] enemies = Find.FactionManager.AllFactionsInViewOrder.Where(o => o != f && o.HostileTo(f) != notEnemies && ((!o.IsPlayer && !o.Hidden) || showAll())).ToArray();
        float size = 22f, x = rect.x, y = enemies.Length * (size + 5f) > rect.width ? rect.y + 8f : Mathf.Round(rect.center.y - size / 2f);
        foreach (Faction enemy in enemies)
        {
            if (x + size > rect.xMax)
            {
                x = rect.x;
                y += size + 4f;
                if (y + size > rect.yMax)
                {
                    break;
                }
            }
            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(x, y, size, size), enemy);
            x += size + 5f;
        }
    }
}

[HarmonyPatch(typeof(FactionUIUtility), nameof(FactionUIUtility.DoWindowContents))]
public static class FactionsWindow_Contents
{
    public static bool Prefix(Rect fillRect, ref Vector2 scrollPosition, ref float scrollViewHeight, Faction scrollToFaction)
    {
        if (!ModernWidgets.Active)
        {
            return true;
        }
        FactionsWindow.Draw(fillRect, ref scrollPosition, ref scrollViewHeight, scrollToFaction);
        return false;
    }
}
