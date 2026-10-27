using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JustDesktop;

// the gizmos with a bar (hemogen and the other gene resources, deathrest, heat and psyfocus) and the mech
// resurrection charges as small cards in the grid, stacked in one column instead of each taking a tall one: the title
// and the numbers on a row, a rounded bar under it. Dragging the bar still sets the target, the hemogen pack toggle
// and tooltips are the game's
public static class Compact
{
    public static bool Is(Gizmo g) => Gizmos.On && (g is Gizmo_Slider || g is GeneGizmo_DeathrestCapacity || g is PsychicEntropyGizmo || g is Gizmo_MechResurrectionCharges);

    public static float Width => Mathf.Round(GizmoGrid.Size * 2.6f);
    public static float Height => GizmoGrid.Size;
    // heat and psyfocus have two bars
    public static float HeightOf(Gizmo g) => g is PsychicEntropyGizmo ? Mathf.Round(GizmoGrid.Size * 1.55f) : Height;

    // where the cards go at the left of the selection card: only when the option doesn't keep them with the gizmos,
    // no tab panel (Needs, Health...) is open there, and there's room
    public static bool LeftOf(out float x)
    {
        x = 0f;
        if (JustDesktopMod.Settings.compactRight || Pane.Current is not { } t)
        {
            return false;
        }
        if (MainButtonDefOf.Inspect.TabWindow is MainTabWindow_Inspect inspect && inspect.OpenTabType != null)
        {
            return false;
        }
        x = Pane.Place(t).x - 5f - Width;
        return x >= 0f;
    }

    // set by the grid for the card being drawn
    public static bool Drawing;

    private static readonly Color Track = new(0f, 0f, 0f, 0.45f);

    private static MethodInfo Getter(string name) => AccessTools.PropertyGetter(typeof(Gizmo_Slider), name);

    private static readonly MethodInfo valuePercent = Getter("ValuePercent");
    private static readonly MethodInfo barColor = Getter("BarColor");
    private static readonly MethodInfo draggable = Getter("IsDraggable");
    private static readonly MethodInfo barLabel = Getter("BarLabel");
    private static readonly MethodInfo increments = Getter("Increments");
    private static readonly MethodInfo dragRange = Getter("DragRange");
    private static readonly MethodInfo targetGet = Getter("Target");
    private static readonly MethodInfo targetSet = AccessTools.PropertySetter(typeof(Gizmo_Slider), "Target");
    private static readonly MethodInfo draggingGet = Getter("DraggingBar");
    private static readonly MethodInfo draggingSet = AccessTools.PropertySetter(typeof(Gizmo_Slider), "DraggingBar");
    private static readonly MethodInfo thresholds = AccessTools.Method(typeof(Gizmo_Slider), "GetBarThresholds");
    private static readonly MethodInfo tooltip = AccessTools.Method(typeof(Gizmo_Slider), "GetTooltip");
    private static readonly MethodInfo header = AccessTools.Method(typeof(Gizmo_Slider), "DrawHeader");
    private static readonly AccessTools.FieldRef<Gizmo_Slider, Rect> barRect = AccessTools.FieldRefAccess<Gizmo_Slider, Rect>("barRect");
    private static readonly AccessTools.FieldRef<GeneGizmo_DeathrestCapacity, Gene_Deathrest> deathrestGene = AccessTools.FieldRefAccess<GeneGizmo_DeathrestCapacity, Gene_Deathrest>("gene");

    private static T Get<T>(MethodInfo m, object o) => (T)m.Invoke(o, null);

    private static Rect Card(Vector2 topLeft)
    {
        Rect rect = new Rect(topLeft.x, topLeft.y, Width, Height);
        Color back = JustDesktopMod.Settings.windowColor;
        back.a = 0.92f;
        GUI.color = back;
        Widgets.DrawAtlas(rect, ModernWidgets.Fill);
        GUI.color = Style.Border;
        Widgets.DrawAtlas(rect, ModernWidgets.Line);
        GUI.color = Color.white;
        return rect;
    }

    private static void Bar(Rect bar, float value, Color color)
    {
        GUI.color = Track;
        Widgets.DrawAtlas(bar, ModernWidgets.Fill);
        float w = Mathf.Round(bar.width * Mathf.Clamp01(value));
        if (w >= 1f)
        {
            // the whole rounded bar, cut where the value ends: a straight edge instead of a rounded end over the track
            GUI.color = new Color(color.r, color.g, color.b, 0.85f);
            Widgets.BeginGroup(new Rect(bar.x, bar.y, w, bar.height));
            Widgets.DrawAtlas(new Rect(0f, 0f, bar.width, bar.height), ModernWidgets.Fill);
            Widgets.EndGroup();
        }
        GUI.color = Color.white;
    }

    public static GizmoResult Slider(Gizmo_Slider g, Vector2 topLeft)
    {
        Rect rect = Card(topLeft);
        Rect inner = rect.ContractedBy(6f, 5f);
        bool overElement = false;
        Text.Font = GameFont.Small;
        // the game's header: the title, and hemogen's pack toggle at its right
        object[] args = { new Rect(inner.x, inner.y, inner.width, 22f), false };
        header.Invoke(g, args);
        overElement = (bool)args[1];

        Rect bar = new Rect(inner.x, inner.y + 25f, inner.width, inner.yMax - inner.y - 25f);
        barRect(g) = bar;
        float value = Get<float>(valuePercent, g);
        Color color = Get<Color>(barColor, g);
        if (color == default)
        {
            color = new Color(0.43f, 0.6f, 0.62f);
        }
        Bar(bar, value, color);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        foreach (float at in Get<IEnumerable<float>>(thresholds, g))
        {
            GUI.DrawTexture(new Rect(bar.x + Mathf.Round(bar.width * at), bar.y + 2f, 1f, bar.height - 4f), BaseContent.WhiteTex);
        }

        if (Get<bool>(draggable, g))
        {
            // the target: a white mark, drag along the bar to move it
            FloatRange range = Get<FloatRange>(dragRange, g);
            float target = Get<float>(targetGet, g);
            bool dragging = Get<bool>(draggingGet, g);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && Mouse.IsOver(bar))
            {
                dragging = true;
                e.Use();
            }
            if (dragging)
            {
                if (Input.GetMouseButton(0))
                {
                    int steps = Mathf.Max(1, Get<int>(increments, g));
                    float pct = Mathf.Clamp01((e.mousePosition.x - bar.x) / bar.width);
                    pct = Mathf.Round(pct * steps) / steps;
                    float set = Mathf.Clamp(pct, range.min, range.max);
                    if (set != target)
                    {
                        targetSet.Invoke(g, new object[] { set });
                        target = set;
                    }
                }
                else
                {
                    dragging = false;
                }
            }
            draggingSet.Invoke(g, new object[] { dragging });
            float x = bar.x + Mathf.Round(bar.width * Mathf.Clamp01(target));
            GUI.color = dragging ? Style.Accent : Color.white;
            GUI.DrawTexture(new Rect(x - 1f, bar.y - 2f, 2f, bar.height + 4f), BaseContent.WhiteTex);
            if (Mouse.IsOver(bar))
            {
                overElement |= dragging;
            }
        }
        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.MiddleCenter;
        GUI.color = Color.white;
        Widgets.Label(bar, Get<string>(barLabel, g));
        Text.Anchor = TextAnchor.UpperLeft;
        Text.Font = GameFont.Small;
        if (Mouse.IsOver(rect) && !overElement)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.04f);
            Widgets.DrawAtlas(rect, ModernWidgets.Fill);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(rect, () => (string)tooltip.Invoke(g, null), Gen.HashCombineInt(g.GetHashCode(), 8573612));
        }
        return new GizmoResult(GizmoState.Clear);
    }

    private static readonly AccessTools.FieldRef<PsychicEntropyGizmo, Pawn_PsychicEntropyTracker> entropyTracker = AccessTools.FieldRefAccess<PsychicEntropyGizmo, Pawn_PsychicEntropyTracker>("tracker");
    private static readonly AccessTools.FieldRef<Gizmo_MechResurrectionCharges, CompAbilityEffect_ResurrectMech> resurrectAbility = AccessTools.FieldRefAccess<Gizmo_MechResurrectionCharges, CompAbilityEffect_ResurrectMech>("ability");
    private static Texture2D limited, unlimited;
    private static bool draggingFocus;

    // the title at the left of a row and a value at its right
    private static void Header(Rect row, string title, string value, float valueRoom = 0f)
    {
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleLeft;
        GUI.color = Style.Text;
        Widgets.Label(row, title);
        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.MiddleRight;
        GUI.color = Style.Dim;
        Widgets.Label(new Rect(row.x, row.y, row.width - valueRoom, row.height), value);
        Text.Anchor = TextAnchor.UpperLeft;
        Text.Font = GameFont.Small;
        GUI.color = Color.white;
    }

    // a part of a bar that blinks, what a psycast under the mouse would add or take
    private static void Preview(Rect bar, float from, float to, Color color)
    {
        from = Mathf.Clamp01(from);
        to = Mathf.Clamp01(to);
        if (to <= from)
        {
            return;
        }
        float t = Mathf.Repeat(Time.time, 0.85f);
        float alpha = t < 0.1f ? t / 0.1f : t >= 0.25f ? 1f - (t - 0.25f) / 0.6f : 1f;
        GUI.color = new Color(color.r, color.g, color.b, alpha * 0.8f);
        GUI.DrawTexture(new Rect(bar.x + Mathf.Round(bar.width * from), bar.y + 2f, Mathf.Round(bar.width * (to - from)), bar.height - 4f), BaseContent.WhiteTex);
        GUI.color = Color.white;
    }

    public static GizmoResult Psyfocus(PsychicEntropyGizmo g, Vector2 topLeft)
    {
        Pawn_PsychicEntropyTracker tracker = entropyTracker(g);
        float height = HeightOf(g);
        Rect rect = new Rect(topLeft.x, topLeft.y, Width, height);
        Color back = JustDesktopMod.Settings.windowColor;
        back.a = 0.92f;
        GUI.color = back;
        Widgets.DrawAtlas(rect, ModernWidgets.Fill);
        GUI.color = Style.Border;
        Widgets.DrawAtlas(rect, ModernWidgets.Line);
        GUI.color = Color.white;
        Rect inner = rect.ContractedBy(6f, 5f);
        float half = (inner.height - 4f) / 2f;
        bool controlled = tracker.Pawn.IsColonistPlayerControlled;
        Ability casting = (MapGizmoUtility.LastMouseOverGizmo as Command_Psycast)?.Ability;

        // heat, with the limit toggle at the right of its title
        Rect heat = new Rect(inner.x, inner.y, inner.width, half);
        float toggle = controlled ? 26f : 0f;
        Header(new Rect(heat.x, heat.y, heat.width, 20f), "PsychicEntropyShort".Translate(), tracker.EntropyValue.ToString("F0") + " / " + tracker.MaxEntropy.ToString("F0"), toggle);
        if (controlled)
        {
            limited ??= ContentFinder<Texture2D>.Get("UI/Icons/EntropyLimit/Limited");
            unlimited ??= ContentFinder<Texture2D>.Get("UI/Icons/EntropyLimit/Unlimited");
            Rect button = new Rect(heat.xMax - 18f, heat.y + 1f, 18f, 18f);
            GUI.color = Mouse.IsOver(button) ? Color.white : Style.Text;
            GUI.DrawTexture(button, tracker.limitEntropyAmount ? limited : unlimited);
            GUI.color = Color.white;
            if (Widgets.ButtonInvisible(button))
            {
                tracker.limitEntropyAmount = !tracker.limitEntropyAmount;
                (tracker.limitEntropyAmount ? SoundDefOf.Tick_Low : SoundDefOf.Tick_High).PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegionByKey(button, "PawnTooltipPsychicEntropyLimit");
        }
        Rect heatBar = new Rect(heat.x, heat.y + 22f, heat.width, heat.height - 22f);
        float entropy = tracker.EntropyRelativeValue;
        Bar(heatBar, Mathf.Min(entropy, 1f), new Color(0.78f, 0.45f, 0.42f));
        if (entropy > 1f)
        {
            Bar(heatBar, Mathf.Min(entropy - 1f, 1f), new Color(0.9f, 0.25f, 0.2f));
        }
        if (casting != null && casting.def.EntropyGain > float.Epsilon)
        {
            float now = entropy > 1f ? entropy - 1f : entropy;
            float after = tracker.EntropyToRelativeValue(tracker.EntropyValue + casting.def.EntropyGain) - (entropy > 1f ? 1f : 0f);
            Preview(heatBar, now, after, new Color(0.95f, 0.85f, 0.75f));
        }
        TooltipHandler.TipRegion(heat, () =>
        {
            float seconds = tracker.EntropyValue / tracker.RecoveryRate;
            TaggedString tip = ("PsychicEntropy".Translate() + ": ").Colorize(ColoredText.TipSectionTitleColor) + Mathf.Round(tracker.EntropyValue) + " / " + Mathf.Round(tracker.MaxEntropy);
            tip += "\n" + "PawnTooltipPsychicEntropyStats".Translate(tracker.RecoveryRate.ToString("0.#"), Mathf.Round(seconds));
            return (tip + ("\n\n" + "PawnTooltipPsychicEntropyDesc".Translate())).Resolve();
        }, Gen.HashCombineInt(tracker.GetHashCode(), 133858));

        // psyfocus, the target dragged along the bar like the game's
        Rect focus = new Rect(inner.x, heat.yMax + 4f, inner.width, half);
        Header(new Rect(focus.x, focus.y, focus.width, 20f), "PsyfocusLabelGizmo".Translate(), tracker.CurrentPsyfocus.ToStringPercent("0"));
        Rect focusBar = new Rect(focus.x, focus.y + 22f, focus.width, focus.height - 22f);
        Bar(focusBar, tracker.CurrentPsyfocus, new Color(0.45f, 0.72f, 0.75f));
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        foreach (float at in Pawn_PsychicEntropyTracker.PsyfocusBandPercentages)
        {
            if (at > 0f && at < 1f)
            {
                GUI.DrawTexture(new Rect(focusBar.x + Mathf.Round(focusBar.width * at), focusBar.y + 2f, 1f, focusBar.height - 4f), BaseContent.WhiteTex);
            }
        }
        GUI.color = Color.white;
        if (casting != null && casting.def.PsyfocusCostRange.min > float.Epsilon)
        {
            float left = Mathf.Max(tracker.CurrentPsyfocus - casting.def.PsyfocusCostRange.min, 0f);
            Preview(focusBar, left, tracker.CurrentPsyfocus, new Color(0.75f, 0.95f, 0.95f));
        }
        if (controlled)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && Mouse.IsOver(focusBar))
            {
                draggingFocus = true;
                e.Use();
            }
            if (draggingFocus)
            {
                if (Input.GetMouseButton(0))
                {
                    float pct = Mathf.Round(Mathf.Clamp01((e.mousePosition.x - focusBar.x) / focusBar.width) * 20f) / 20f;
                    if (pct != tracker.TargetPsyfocus)
                    {
                        tracker.SetPsyfocusTarget(pct);
                        PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.MeditationDesiredPsyfocus, KnowledgeAmount.Total);
                    }
                }
                else
                {
                    draggingFocus = false;
                }
            }
            float x = focusBar.x + Mathf.Round(focusBar.width * Mathf.Clamp01(tracker.TargetPsyfocus));
            GUI.color = draggingFocus ? Style.Accent : Color.white;
            GUI.DrawTexture(new Rect(x - 1f, focusBar.y - 2f, 2f, focusBar.height + 4f), BaseContent.WhiteTex);
            GUI.color = Color.white;
        }
        UIHighlighter.HighlightOpportunity(focusBar, "PsyfocusBar");
        TooltipHandler.TipRegion(focus, () => tracker.PsyfocusTipString(), Gen.HashCombineInt(tracker.GetHashCode(), 133873));
        return new GizmoResult(GizmoState.Clear);
    }

    public static GizmoResult MechCharges(Gizmo_MechResurrectionCharges g, Vector2 topLeft)
    {
        Rect rect = Card(topLeft);
        Rect inner = rect.ContractedBy(8f, 5f);
        Text.Font = GameFont.Medium;
        string charges = resurrectAbility(g).ChargesRemaining.ToString();
        float numberW = Text.CalcSize(charges).x + 4f;
        Text.Anchor = TextAnchor.MiddleRight;
        GUI.color = Color.white;
        Widgets.Label(inner, charges);
        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.MiddleLeft;
        GUI.color = Style.Text;
        Widgets.Label(new Rect(inner.x, inner.y, inner.width - numberW - 4f, inner.height), "MechResurrectionCharges".Translate());
        Text.Anchor = TextAnchor.UpperLeft;
        Text.Font = GameFont.Small;
        GUI.color = Color.white;
        return new GizmoResult(GizmoState.Clear);
    }

    public static GizmoResult Deathrest(GeneGizmo_DeathrestCapacity g, Vector2 topLeft)
    {
        Gene_Deathrest gene = deathrestGene(g);
        Rect rect = Card(topLeft);
        Rect inner = rect.ContractedBy(6f, 5f);
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleLeft;
        GUI.color = Style.Text;
        Widgets.Label(new Rect(inner.x, inner.y, inner.width, 22f), "Deathrest".Translate().CapitalizeFirst());
        // the buildings it's connected to, at the right of the title
        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.MiddleRight;
        GUI.color = Style.Dim;
        Rect buildings = new Rect(inner.xMax - 60f, inner.y, 60f, 22f);
        Widgets.Label(buildings, gene.CurrentCapacity + " / " + gene.DeathrestCapacity);
        Text.Anchor = TextAnchor.UpperLeft;
        if (Mouse.IsOver(buildings))
        {
            TooltipHandler.TipRegion(buildings, "Buildings".Translate().CapitalizeFirst() + ": " + gene.CurrentCapacity + " / " + gene.DeathrestCapacity + "\n\n" + "DeathrestCapacityDesc".Translate() + "\n\n" + "PawnIsConnectedToBuildings".Translate(gene.pawn.Named("PAWN"), gene.CurrentCapacity.Named("CURRENT"), gene.DeathrestCapacity.Named("MAX")));
        }
        Rect bar = new Rect(inner.x, inner.y + 25f, inner.width, inner.yMax - inner.y - 25f);
        Need need = gene.DeathrestNeed;
        if (need != null)
        {
            Bar(bar, need.CurLevelPercentage, new Color(0.3f, 0.75f, 0.85f));
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;
            Widgets.Label(bar, need.CurLevelPercentage.ToStringPercent());
            Text.Anchor = TextAnchor.UpperLeft;
            if (Mouse.IsOver(bar))
            {
                TooltipHandler.TipRegion(bar, need.GetTipString());
            }
        }
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        return new GizmoResult(GizmoState.Clear);
    }
}

[HarmonyPatch(typeof(Gizmo_Slider), nameof(Gizmo_Slider.GizmoOnGUI))]
public static class Compact_Slider
{
    public static bool Prefix(Gizmo_Slider __instance, Vector2 topLeft, ref GizmoResult __result)
    {
        if (!Compact.Drawing)
        {
            return true;
        }
        __result = Compact.Slider(__instance, topLeft);
        return false;
    }
}

[HarmonyPatch(typeof(GeneGizmo_DeathrestCapacity), nameof(GeneGizmo_DeathrestCapacity.GizmoOnGUI))]
public static class Compact_Deathrest
{
    public static bool Prefix(GeneGizmo_DeathrestCapacity __instance, Vector2 topLeft, ref GizmoResult __result)
    {
        if (!Compact.Drawing)
        {
            return true;
        }
        __result = Compact.Deathrest(__instance, topLeft);
        return false;
    }
}

[HarmonyPatch(typeof(PsychicEntropyGizmo), nameof(PsychicEntropyGizmo.GizmoOnGUI))]
public static class Compact_Psyfocus
{
    public static bool Prefix(PsychicEntropyGizmo __instance, Vector2 topLeft, ref GizmoResult __result)
    {
        if (!Compact.Drawing)
        {
            return true;
        }
        __result = Compact.Psyfocus(__instance, topLeft);
        return false;
    }
}

[HarmonyPatch(typeof(Gizmo_MechResurrectionCharges), nameof(Gizmo_MechResurrectionCharges.GizmoOnGUI))]
public static class Compact_MechCharges
{
    public static bool Prefix(Gizmo_MechResurrectionCharges __instance, Vector2 topLeft, ref GizmoResult __result)
    {
        if (!Compact.Drawing)
        {
            return true;
        }
        __result = Compact.MechCharges(__instance, topLeft);
        return false;
    }
}
