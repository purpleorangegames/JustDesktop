using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// trading, forming a caravan or loading transport pods: hovering a row's name shows a card next to the window, a
// pawn's bio (traits, backstory, skills) or an item's details. Your things get it at the left of the window, the
// trader's at the right
public static class TransferHover
{
    private static Thing hovered;
    private static Rect window;
    private static bool right;
    private static float mouseY;
    private static int frame = -1;

    private static float ItemWidth => Mathf.Round(300f * Pane.Scale);

    // test: this thing's row counts as hovered
    public static Thing TestHover;

    public static void Hovering(Transferable trad, Rect idRect)
    {
        if (!JustDesktopMod.Settings.tradeHoverCard || !(Mouse.IsOver(idRect) || TestHover != null && trad?.AnyThing == TestHover) || trad?.AnyThing == null || Find.WindowStack.currentlyDrawnWindow is not { } w)
        {
            return;
        }
        hovered = trad.AnyThing;
        window = w.windowRect;
        // the trader's things go right, ours left; something both have goes by which half of the window the mouse is in
        Vector2 mouse = UI.MousePositionOnUIInverted;
        if (trad is Tradeable t)
        {
            bool ours = t.CountHeldBy(Transactor.Colony) > 0, theirs = t.CountHeldBy(Transactor.Trader) > 0;
            right = theirs && (!ours || mouse.x > window.center.x);
        }
        else
        {
            right = false;
        }
        mouseY = mouse.y;
        frame = Time.frameCount;
    }

    public static void OnGUI()
    {
        if (frame != Time.frameCount || hovered == null)
        {
            return;
        }
        Thing t = hovered;
        Pawn p = t as Pawn;
        bool bio = p?.story != null;
        Vector2 size = bio ? CharacterPanel.Size(p) : new Vector2(ItemWidth, ItemHeight(t));
        size.y = Mathf.Min(size.y, UI.screenHeight - 16f);
        float x = right ? window.xMax + 8f : window.x - 8f - size.x;
        x = Mathf.Clamp(x, 4f, UI.screenWidth - size.x - 4f);
        float y = Mathf.Clamp(mouseY - 40f, 8f, UI.screenHeight - size.y - 8f);
        Rect rect = new Rect(x, y, size.x, size.y);
        Find.WindowStack.ImmediateWindow(52983117, rect, WindowLayer.Super, () =>
        {
            Rect all = rect.AtZero();
            Pane.CardBack(all);
            if (bio)
            {
                CharacterPanel.Draw(all, p);
            }
            else
            {
                DrawItem(all, t);
            }
        }, doBackground: false, absorbInputAroundWindow: false, shadowAlpha: 0f);
    }

    // ---- an item (or an animal) ----

    private const float Pad = 10f, Line = 18f;

    private static List<(string label, string value)> Facts(Thing t)
    {
        List<(string, string)> facts = new();
        if (t is Pawn a)
        {
            facts.Add(("Age".Translate(), a.ageTracker.AgeBiologicalYears.ToString()));
            if (a.gender != Gender.None)
            {
                facts.Add(("Gender".Translate().CapitalizeFirst(), a.GetGenderLabel().CapitalizeFirst()));
            }
            facts.Add(("Health".Translate(), a.health.summaryHealth.SummaryHealthPercent.ToStringPercent()));
            if (a.training != null)
            {
                List<string> learned = DefDatabase<TrainableDef>.AllDefsListForReading.Where(d => a.training.HasLearned(d)).Select(d => d.label).ToList();
                if (learned.Count > 0)
                {
                    facts.Add(("Trained", learned.ToCommaList()));
                }
            }
            return facts;
        }
        if (t.TryGetQuality(out QualityCategory q))
        {
            facts.Add(("Quality".Translate(), q.GetLabel().CapitalizeFirst()));
        }
        if (t.def.useHitPoints && t.MaxHitPoints > 0)
        {
            facts.Add(("HitPointsBasic".Translate().CapitalizeFirst(), t.HitPoints + " / " + t.MaxHitPoints));
        }
        facts.Add(("MarketValueTip".Translate().CapitalizeFirst(), t.MarketValue.ToStringMoney()));
        facts.Add(("Mass".Translate(), t.GetStatValue(StatDefOf.Mass).ToStringMass()));
        return facts;
    }

    private static string Description(Thing t) => (t is Pawn ? t.def.description : t.DescriptionFlavor ?? t.def.description)?.Trim() ?? "";

    private static float TextHeight(Thing t)
    {
        GameFont f = Text.Font;
        Text.Font = GameFont.Tiny;
        float h = Mathf.Min(Text.CalcHeight(Description(t), ItemWidth - Pad * 2f), 160f);
        Text.Font = f;
        return h;
    }

    private static float ItemHeight(Thing t)
    {
        float h = Pad + 24f + 4f + Facts(t).Count * Line + 6f;
        if (t is Genepack { GeneSet: { } set })
        {
            h += 18f + set.GenesListForReading.Count * Line + 6f;
        }
        string d = Description(t);
        if (!d.NullOrEmpty())
        {
            h += TextHeight(t) + 4f;
        }
        return Mathf.Round(h + Pad);
    }

    private static void DrawItem(Rect all, Thing t)
    {
        Rect inner = all.ContractedBy(Pad);
        float y = inner.y;
        Rect icon = new Rect(inner.x, y, 24f, 24f);
        Widgets.ThingIcon(icon, t);
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleLeft;
        Text.WordWrap = false;
        Widgets.Label(new Rect(icon.xMax + 6f, y, inner.width - 30f, 24f), t.LabelCapNoCount.Truncate(inner.width - 30f));
        Text.WordWrap = true;
        y += 28f;
        Text.Font = GameFont.Tiny;
        foreach ((string label, string value) in Facts(t))
        {
            GUI.color = Style.Dim;
            Widgets.Label(new Rect(inner.x, y, inner.width * 0.45f, Line), label);
            GUI.color = Style.Text;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(inner.x + inner.width * 0.4f, y, inner.width * 0.6f, Line), value.Truncate(inner.width * 0.6f));
            Text.Anchor = TextAnchor.MiddleLeft;
            y += Line;
        }
        y += 6f;
        if (t is Genepack { GeneSet: { } set })
        {
            GUI.color = Style.Dim;
            Widgets.Label(new Rect(inner.x, y, inner.width, 16f), "Genes");
            y += 18f;
            foreach (GeneDef g in set.GenesListForReading)
            {
                string reason = "";
                Color c = JustDesktopMod.Settings.geneColors ? GeneColors.StatusOf(g, out reason) : Style.Dim;
                GUI.color = c;
                GUI.DrawTexture(new Rect(inner.x + 2f, y + 4f, 10f, 10f), GeneColors.DotTex);
                GUI.color = Style.Text;
                Widgets.Label(new Rect(inner.x + 18f, y, inner.width * 0.5f, Line), g.LabelCap.ToString().Truncate(inner.width * 0.5f));
                GUI.color = Style.Dim;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(inner.x + inner.width * 0.45f, y, inner.width * 0.55f, Line), reason);
                Text.Anchor = TextAnchor.MiddleLeft;
                y += Line;
            }
            y += 6f;
        }
        string d = Description(t);
        if (!d.NullOrEmpty())
        {
            GUI.color = Style.Dim;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(inner.x, y, inner.width, TextHeight(t)), d);
        }
        Text.Anchor = TextAnchor.UpperLeft;
        Text.Font = GameFont.Small;
        GUI.color = Color.white;
    }
}

[HarmonyPatch(typeof(TransferableUIUtility), nameof(TransferableUIUtility.DrawTransferableInfo))]
public static class TransferHover_Row
{
    public static void Postfix(Transferable trad, Rect idRect) => TransferHover.Hovering(trad, idRect);
}

[HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
public static class TransferHover_OnGUI
{
    public static void Postfix() => TransferHover.OnGUI();
}
