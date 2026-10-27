using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JustDesktop;

// the Health tab laid out to fit, as wide as the card: overview and operations as two small tabs; food policy,
// medicine and self-tend in one row; pain and bleeding in one line; the capacities as bars in two columns; and the
// conditions by body part as rows with the game's colors, icons, tooltips and click to the combat log.
// Operations lists the surgery bills one line each, with the game's buttons
public static class HealthPanel
{
    private static float Scale => Pane.Scale;
    private static float Width => Mathf.Round(400f * Scale);
    private static float Row => Pane.RowHeight + 2f;
    private static float Line => Mathf.Round(20f * Scale);
    private static float Heading => Mathf.Round(18f * Scale);
    private static float Section => Mathf.Round(8f * Scale);
    private static float Control => Mathf.Round(22f * Scale);
    private const int MaxConditionRows = 14;
    private const int MaxBillRows = 12;

    private static readonly AccessTools.FieldRef<bool> onOperationTab = AccessTools.StaticFieldRefAccess<bool>(AccessTools.Field(typeof(HealthCardUtility), "onOperationTab"));
    private static readonly Func<ITab_Pawn_Health, bool> allowOperations = AccessTools.MethodDelegate<Func<ITab_Pawn_Health, bool>>(AccessTools.Method(typeof(ITab_Pawn_Health), "ShouldAllowOperations"));
    private static readonly Func<Pawn, bool, IEnumerable<IGrouping<BodyPartRecord, Hediff>>> groupsInOrder = AccessTools.MethodDelegate<Func<Pawn, bool, IEnumerable<IGrouping<BodyPartRecord, Hediff>>>>(AccessTools.Method(typeof(HealthCardUtility), "VisibleHediffGroupsInOrder"));
    private static readonly Func<Pawn, Thing, RecipeDef, IEnumerable<ThingDef>, AcceptanceReport, int, BodyPartRecord, FloatMenuOption> surgeryOption = AccessTools.MethodDelegate<Func<Pawn, Thing, RecipeDef, IEnumerable<ThingDef>, AcceptanceReport, int, BodyPartRecord, FloatMenuOption>>(AccessTools.Method(typeof(HealthCardUtility), "GenerateSurgeryOption"));
    private static readonly Func<Bill, string> billStatus = AccessTools.MethodDelegate<Func<Bill, string>>(AccessTools.PropertyGetter(typeof(Bill), "StatusString"));
    private static readonly Func<Pawn, BodyPartRecord, string> partTip = AccessTools.MethodDelegate<Func<Pawn, BodyPartRecord, string>>(AccessTools.Method(typeof(HealthCardUtility), "GetTooltip"));
    private static readonly Func<IEnumerable<Hediff>, Pawn, bool> canClick = AccessTools.MethodDelegate<Func<IEnumerable<Hediff>, Pawn, bool>>(AccessTools.Method(typeof(HealthCardUtility), "CanEntryBeClicked"));
    private static readonly Action<IEnumerable<Hediff>, Pawn> clicked = AccessTools.MethodDelegate<Action<IEnumerable<Hediff>, Pawn>>(AccessTools.Method(typeof(HealthCardUtility), "EntryClicked"));
    private static Texture2D _bleedingIcon;
    private static Texture2D BleedingIcon => _bleedingIcon ??= ContentFinder<Texture2D>.Get("UI/Icons/Medical/Bleeding");
    private struct Condition
    {
        public BodyPartRecord Part;
        public bool FirstOfPart;
        public List<Hediff> Hediffs;
        // the part's health when it's hurt, shown under its name
        public float PartHealth;
        // how far along to deadly, and for a disease the immunity against it; -1 when not shown
        public float Severity, Immunity;
        public float Height;
    }

    private static readonly List<Condition> conditions = new();
    private static readonly List<PawnCapacityDef> capacities = new();
    private static Vector2 scroll;

    private static bool Operations(ITab_Pawn_Health tab)
    {
        if (!allowOperations(tab))
        {
            onOperationTab() = false;
        }
        return onOperationTab();
    }

    private static void Collect(Pawn p)
    {
        conditions.Clear();
        foreach (IGrouping<BodyPartRecord, Hediff> part in groupsInOrder(p, HealthCardUtility.ShowBloodLoss(Pane.Current ?? p)))
        {
            bool first = true;
            foreach (IGrouping<int, Hediff> same in part.GroupBy(h => h.UIGroupKey))
            {
                Condition c = new Condition { Part = part.Key, FirstOfPart = first, Hediffs = same.ToList(), PartHealth = -1f, Severity = -1f, Immunity = -1f };
                if (first && part.Key != null)
                {
                    float health = p.health.hediffSet.GetPartHealth(part.Key), max = part.Key.def.GetMaxHealth(p);
                    if (health > 0f && health < max)
                    {
                        c.PartHealth = health / max;
                    }
                }
                Hediff h = c.Hediffs[0];
                if (h is not Hediff_Injury && h is not Hediff_MissingPart)
                {
                    HediffComp_Immunizable immunizable = h.TryGetComp<HediffComp_Immunizable>();
                    float deadly = h.def.lethalSeverity > 0f ? h.def.lethalSeverity : immunizable != null && h.def.maxSeverity < float.MaxValue ? h.def.maxSeverity : 0f;
                    if (deadly > 0f)
                    {
                        c.Severity = Mathf.Clamp01(h.Severity / deadly);
                        c.Immunity = immunizable != null ? Mathf.Clamp01(immunizable.Immunity) : -1f;
                    }
                }
                int bars = Mathf.Max(c.PartHealth >= 0f ? 1 : 0, (c.Severity >= 0f ? 1 : 0) + (c.Immunity >= 0f ? 1 : 0));
                c.Height = Line + (bars > 0 ? bars * (Thin + 1f) + 2f * Scale : 0f);
                conditions.Add(c);
                first = false;
            }
        }
        capacities.Clear();
        if (p.Dead)
        {
            return;
        }
        RaceProperties race = p.def.race;
        foreach (PawnCapacityDef def in DefDatabase<PawnCapacityDef>.AllDefs.OrderBy(d => d.listOrder))
        {
            bool shown = race.Humanlike ? def.showOnHumanlikes : race.Animal ? def.showOnAnimals : race.IsAnomalyEntity ? def.showOnAnomalyEntities : race.IsDrone ? def.showOnDrones : def.showOnMechanoids;
            if (shown && PawnCapacityUtility.BodyCanEverDoCapacity(p.RaceProps.body, def))
            {
                capacities.Add(def);
            }
        }
    }

    private static bool FoodPolicy(Pawn p) => p.foodRestriction != null && p.foodRestriction.Configurable && !p.DevelopmentalStage.Baby() && p.needs?.food != null && (!p.IsMutant || !p.mutant.Def.disablePolicies);

    private static bool Medicine(Pawn p)
    {
        bool ours = p.Faction == Faction.OfPlayer || p.HostFaction == Faction.OfPlayer;
        bool inOurBed = p.NonHumanlikeOrWildMan() && p.InBed() && p.CurrentBed().Faction == Faction.OfPlayer;
        return p.RaceProps.IsFlesh && (ours || inOurBed) && (!p.IsMutant || p.mutant.Def.entitledToMedicalCare) && p.playerSettings != null && !p.Dead;
    }

    private static bool SelfTend(Pawn p) => p.IsColonist && !p.Dead && !p.DevelopmentalStage.Baby() && p.playerSettings != null;

    // as tall as the first rows, the rest scroll
    private static float ConditionsHeight => conditions.Count == 0 ? Line : conditions.Take(MaxConditionRows).Sum(c => c.Height);
    private static float Thin => Mathf.Max(2f, Mathf.Round(3f * Scale));

    public static Vector2 Size(ITab_Pawn_Health tab, Pawn p)
    {
        if (Operations(tab))
        {
            float rows = Mathf.Max(1, Mathf.Min(p.BillStack.Count, MaxBillRows));
            return new Vector2(Width, Mathf.Round(Pane.Pad * 2f + 24f * Scale + Section + Control + Section + rows * (Line + 2f)));
        }
        Collect(p);
        float h = Pane.Pad + 24f * Scale + Section;
        if (FoodPolicy(p) || Medicine(p) || SelfTend(p))
        {
            h += Control + Section;
        }
        h += 16f * Scale + Section;
        if (capacities.Count > 0)
        {
            h += Mathf.Ceil(capacities.Count / 2f) * Row + Section;
        }
        h += Heading + ConditionsHeight;
        return new Vector2(Width, Mathf.Round(h + Pane.Pad));
    }

    public static void Draw(Rect all, ITab_Pawn_Health tab, Pawn p)
    {
        Rect inner = all.ContractedBy(Pane.Pad);
        Widgets.BeginGroup(inner);
        try
        {
            Rect area = inner.AtZero();
            Text.Font = Pane.NameFont;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(0f, 0f, area.width / 2f, 24f * Scale), "TabHealth".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = Pane.SmallFont;
            Tabs(new Rect(0f, 0f, area.width - 24f, 24f * Scale), tab, p);
            float y = 24f * Scale + Section;
            if (Operations(tab))
            {
                PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.MedicalOperations, KnowledgeAmount.FrameDisplayed);
                Bills(new Rect(0f, y, area.width, area.height - y), p);
                return;
            }
            Collect(p);
            y = Settings(y, area.width, p);
            y = Status(y, area.width, p);
            y = Capacities(y, area.width, p);
            Conditions(new Rect(0f, y, area.width, area.height - y), p);
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

    // overview and operations as two small tabs at the right of the title
    private static void Tabs(Rect row, ITab_Pawn_Health tab, Pawn p)
    {
        if (!allowOperations(tab))
        {
            return;
        }
        string operations = p.RaceProps.IsMechanoid ? "MedicalOperationsMechanoidsShort".Translate() : "MedicalOperationsShort".Translate();
        if (p.BillStack.Count > 0)
        {
            operations += " (" + p.BillStack.Count + ")";
        }
        string overview = "HealthOverview".Translate();
        float h = Control;
        float w2 = Text.CalcSize(operations).x + 16f * Scale, w1 = Text.CalcSize(overview).x + 16f * Scale;
        Rect second = new Rect(row.xMax - w2, Mathf.Round(row.center.y - h / 2f), w2, h);
        Rect first = new Rect(second.x - 2f - w1, second.y, w1, h);
        if (Segment(first, overview, !onOperationTab()))
        {
            onOperationTab() = false;
        }
        if (Segment(second, operations, onOperationTab()))
        {
            onOperationTab() = true;
        }
    }

    private static bool Segment(Rect r, string label, bool on)
    {
        bool over = Mouse.IsOver(r);
        GUI.color = on ? Style.Faded(0.3f) : over ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.06f);
        Widgets.DrawAtlas(r, ModernWidgets.Fill);
        GUI.color = on || over ? Color.white : Style.Text;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(r, label);
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        return !on && Widgets.ButtonInvisible(r);
    }

    // food policy, medicine and self-tend in a row
    private static float Settings(float y, float width, Pawn p)
    {
        bool food = FoodPolicy(p), medicine = Medicine(p), selfTend = SelfTend(p);
        if (!food && !medicine && !selfTend)
        {
            return y;
        }
        float x = 0f;
        if (food)
        {
            string label = "AllowFood".Translate() + ": " + p.foodRestriction.CurrentFoodPolicy.label;
            float w = Mathf.Min(Text.CalcSize(label).x + 16f * Scale, width * 0.6f);
            Rect r = new Rect(x, y, w, Control);
            TooltipHandler.TipRegionByKey(r, "FoodRestrictionDescription");
            if (Widgets.ButtonText(r, label.Truncate(w - 8f)))
            {
                List<FloatMenuOption> options = new();
                foreach (FoodPolicy policy in Current.Game.foodRestrictionDatabase.AllFoodRestrictions)
                {
                    FoodPolicy local = policy;
                    options.Add(new FloatMenuOption(policy.label, () => p.foodRestriction.CurrentFoodPolicy = local));
                }
                options.Add(new FloatMenuOption("ManageFoodPolicies".Translate() + "...", () => Find.WindowStack.Add(new Dialog_ManageFoodPolicies(p.foodRestriction.CurrentFoodPolicy))));
                Find.WindowStack.Add(new FloatMenu(options));
            }
            x += w + 6f * Scale;
        }
        if (medicine)
        {
            Rect r = new Rect(x, y, Control, Control);
            TooltipHandler.TipRegionByKey(r, "MedicineQualityDescription");
            Widgets.DrawButtonGraphic(r);
            MedicalCareUtility.MedicalCareSelectButton(r, p);
            x += Control + 6f * Scale;
        }
        if (selfTend)
        {
            Pane.SelfTend(new Rect(x, y, Control, Control), p);
        }
        return y + Control + Section;
    }

    // pain, and how fast they're bleeding and how long until they bleed out
    private static float Status(float y, float width, Pawn p)
    {
        Rect r = new Rect(0f, y, width, 16f * Scale);
        float x = 0f;
        Text.Anchor = TextAnchor.MiddleLeft;
        if (p.def.race.IsFlesh)
        {
            Pair<string, Color> pain = HealthCardUtility.GetPainLabel(p);
            string label = "PainLevel".Translate() + ":";
            GUI.color = Style.Dim;
            Widgets.Label(new Rect(x, r.y, width, r.height), label);
            x += Text.CalcSize(label).x + 4f * Scale;
            GUI.color = pain.Second;
            Widgets.Label(new Rect(x, r.y, width - x, r.height), pain.First);
            Rect painRect = new Rect(0f, r.y, x + Text.CalcSize(pain.First).x, r.height);
            TooltipHandler.TipRegion(painRect, HealthCardUtility.GetPainTip(p));
            x = painRect.xMax + 14f * Scale;
        }
        float bleeding = p.health.hediffSet.BleedRateTotal;
        if (bleeding > 0.01f)
        {
            string text = "BleedingRate".Translate() + ": " + bleeding.ToStringPercent() + "/" + "LetterDay".Translate();
            int ticks = HealthUtility.TicksUntilDeathDueToBloodLoss(p);
            if (ModsConfig.BiotechActive && p.genes != null && p.genes.HasActiveGene(GeneDefOf.Deathless))
            {
                text += " (" + "Deathless".Translate() + ")";
            }
            else
            {
                text += " (" + (ticks >= 60000 ? "WontBleedOutSoon".Translate() : "TimeToDeath".Translate(ticks.ToStringTicksToPeriod())) + ")";
            }
            GUI.color = HealthUtility.RedColor;
            Text.WordWrap = false;
            Widgets.Label(new Rect(x, r.y, width - x, r.height), text.Truncate(width - x));
            Text.WordWrap = true;
        }
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        return y + r.height + Section;
    }

    private static float Capacities(float y, float width, Pawn p)
    {
        if (capacities.Count == 0)
        {
            return y;
        }
        float gap = Mathf.Round(10f * Scale);
        float col = Mathf.Floor((width - gap) / 2f);
        int rows = Mathf.CeilToInt(capacities.Count / 2f);
        for (int i = 0; i < capacities.Count; i++)
        {
            PawnCapacityDef def = capacities[i];
            Pair<string, Color> label = HealthCardUtility.GetEfficiencyLabel(p, def);
            float level = p.health.capacities.GetLevel(def);
            Rect r = new Rect(i / rows * (col + gap), y + i % rows * Row, col, Pane.RowHeight);
            Pane.DrawBar(r, new Pane.Bar { Label = def.GetLabelFor(p).CapitalizeFirst(), Value = Mathf.Clamp01(level), ValueText = label.First, Color = label.Second, Tip = Mouse.IsOver(r) ? HealthCardUtility.GetPawnCapacityTip(p, def) : null }, null);
        }
        return y + rows * Row + Section;
    }

    // the conditions by body part: the part (colored by its condition) on the first row of its group, then each
    // condition with its icons; the game's tooltips, and a click goes to the combat log entry that caused it
    private static void Conditions(Rect rect, Pawn p)
    {
        GUI.color = Style.Dim;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, Heading), "Conditions");
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        Rect list = new Rect(rect.x, rect.y + Heading, rect.width, ConditionsHeight);
        if (conditions.Count == 0)
        {
            GUI.color = ColoredText.SubtleGrayColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(list.ContractedBy(5f * Scale, 0f), "NoHealthConditions".Translate().CapitalizeFirst());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            return;
        }
        bool scrolls = conditions.Count > MaxConditionRows;
        Rect view = new Rect(0f, 0f, list.width - (scrolls ? 16f : 0f), conditions.Sum(c => c.Height));
        Widgets.BeginScrollView(list, ref scroll, view, scrolls);
        float partWidth = Mathf.Round(view.width * 0.34f);
        float y = 0f;
        int group = -1;
        for (int i = 0; i < conditions.Count; i++)
        {
            Condition c = conditions[i];
            // every other body part on a faint background, all its rows together, so the parts are easy to tell apart
            if (c.FirstOfPart)
            {
                group++;
                if (group % 2 == 0)
                {
                    float span = c.Height;
                    for (int j = i + 1; j < conditions.Count && !conditions[j].FirstOfPart; j++)
                    {
                        span += conditions[j].Height;
                    }
                    GUI.color = new Color(1f, 1f, 1f, 0.05f);
                    Widgets.DrawAtlas(new Rect(0f, y, view.width, span), ModernWidgets.Fill);
                    GUI.color = Color.white;
                }
            }
            // the text on the first line, the bars under it
            Rect row = new Rect(0f, y, view.width, Line);
            y += c.Height;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            if (c.FirstOfPart)
            {
                GUI.color = c.Part != null ? HealthUtility.GetPartConditionLabel(p, c.Part).Second : HealthUtility.RedColor;
                string part = c.Part != null ? c.Part.LabelCap : "WholeBody".Translate().ToString();
                Widgets.Label(new Rect(row.x + 5f * Scale, row.y, partWidth - 5f * Scale, row.height), part.Truncate(partWidth - 5f * Scale));
            }
            Hediff first = c.Hediffs[0];
            float icons = Icons(row, c);
            Rect label = new Rect(partWidth, row.y, row.width - partWidth - icons - 4f * Scale, row.height);
            float barY = row.yMax + 1f;
            if (c.PartHealth >= 0f)
            {
                ThinBar(new Rect(row.x + 5f * Scale, barY, partWidth - 14f * Scale, Thin), c.PartHealth, Color.Lerp(new Color(0.85f, 0.3f, 0.3f), new Color(0.38f, 0.75f, 0.44f), c.PartHealth));
            }
            Rect bars = new Rect(label.x + 4f * Scale, barY, label.width - 8f * Scale, Thin);
            if (c.Severity >= 0f)
            {
                ThinBar(bars, c.Severity, Severe);
                bars.y += Thin + 1f;
            }
            if (c.Immunity >= 0f)
            {
                ThinBar(bars, c.Immunity, Immune);
            }
            if (Mouse.IsOver(label))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.06f);
                Widgets.DrawAtlas(label, ModernWidgets.Fill);
            }
            GUI.color = first.LabelColor;
            string text = first.LabelCap + (c.Hediffs.Count > 1 ? " x" + c.Hediffs.Count : "");
            Widgets.Label(new Rect(label.x + 4f * Scale, label.y, label.width - 4f * Scale, label.height), text.Truncate(label.width - 4f * Scale));
            Text.WordWrap = true;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (Mouse.IsOver(label))
            {
                int n = 0;
                foreach (Hediff h in c.Hediffs)
                {
                    Hediff local = h;
                    TooltipHandler.TipRegion(label, new TipSignal(() => local.GetTooltip(p, false), (int)row.y + 7857 + n++, TooltipPriority.Default));
                }
                if (c.Part != null)
                {
                    BodyPartRecord part = c.Part;
                    TooltipHandler.TipRegion(label, new TipSignal(() => partTip(p, part), (int)row.y + 7857 + ++n, TooltipPriority.Pawn));
                }
            }
            if (Widgets.ButtonInvisible(label, canClick(c.Hediffs, p)))
            {
                clicked(c.Hediffs, p);
            }
        }
        Widgets.EndScrollView();
    }

    // ---- operations ----

    private static Vector2 billScroll;

    // add bill (the game's list of surgeries for this pawn), then a line per bill
    private static void Bills(Rect rect, Pawn p)
    {
        BillStack stack = p.BillStack;
        if (stack.Count < 15)
        {
            Rect add = new Rect(rect.x, rect.y, Mathf.Round(150f * Scale), Control);
            if (Widgets.ButtonText(add, "AddBill".Translate()))
            {
                Find.WindowStack.Add(new FloatMenu(SurgeryOptions(p)));
            }
            UIHighlighter.HighlightOpportunity(add, "AddBill");
        }
        Rect list = new Rect(rect.x, rect.y + Control + Section, rect.width, Mathf.Max(1, Mathf.Min(stack.Count, MaxBillRows)) * (Line + 2f));
        if (stack.Count == 0)
        {
            GUI.color = ColoredText.SubtleGrayColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(list.x + 5f * Scale, list.y, list.width, Line), "None".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            return;
        }
        bool scrolls = stack.Count > MaxBillRows;
        Rect view = new Rect(0f, 0f, list.width - (scrolls ? 16f : 0f), stack.Count * (Line + 2f));
        Widgets.BeginScrollView(list, ref billScroll, view, scrolls);
        for (int i = 0; i < stack.Count; i++)
        {
            BillRow(new Rect(0f, i * (Line + 2f), view.width, Line), stack, stack[i], i);
        }
        Widgets.EndScrollView();
    }

    // reorder arrows, the bill and what it's waiting for, then info, suspend and delete
    private static void BillRow(Rect row, BillStack stack, Bill bill, int index)
    {
        bool now = bill.ShouldDoNow();
        GUI.color = Mouse.IsOver(row) ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.04f);
        Widgets.DrawAtlas(row, ModernWidgets.Fill);
        GUI.color = Color.white;
        float icon = Mathf.Round(16f * Scale);
        float y = Mathf.Round(row.center.y - icon / 2f);
        float x = row.x + 4f * Scale;
        Rect up = new Rect(x, y, icon, icon), down = new Rect(x + icon, y, icon, icon);
        if (index > 0)
        {
            if (Widgets.ButtonImage(up, TexButton.ReorderUp, Style.Text))
            {
                stack.Reorder(bill, -1);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegionByKey(up, "ReorderBillUpTip");
        }
        if (index < stack.Count - 1)
        {
            if (Widgets.ButtonImage(down, TexButton.ReorderDown, Style.Text))
            {
                stack.Reorder(bill, 1);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegionByKey(down, "ReorderBillDownTip");
        }
        x = down.xMax + 6f * Scale;

        float right = row.xMax - 4f * Scale;
        Rect Next()
        {
            right -= icon + 4f * Scale;
            return new Rect(right + 4f * Scale, y, icon, icon);
        }
        Rect delete = Next();
        if (Widgets.ButtonImage(delete, TexButton.Delete, Style.Dim, Style.CloseHover))
        {
            stack.Delete(bill);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }
        TooltipHandler.TipRegionByKey(delete, "DeleteBillTip");
        Rect suspend = Next();
        if (Widgets.ButtonImage(suspend, TexButton.Suspend, bill.suspended ? Style.Accent : Style.Dim, Color.white))
        {
            bill.suspended = !bill.suspended;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }
        TooltipHandler.TipRegionByKey(suspend, "SuspendBillTip");
        Widgets.InfoCardButton(Next(), bill.recipe);

        // the bill, dimmed when it won't be done now, then what it's waiting for or that it's suspended
        Rect label = new Rect(x, row.y, right - x, row.height);
        string status = bill.suspended ? "SuspendedCaps".Translate().CapitalizeFirst() : billStatus(bill);
        Text.Anchor = TextAnchor.MiddleLeft;
        Text.WordWrap = false;
        string name = bill.LabelCap;
        float nameWidth = Mathf.Min(Text.CalcSize(name).x, label.width);
        GUI.color = now && !bill.suspended ? Style.Text : Style.Dim;
        Widgets.Label(new Rect(label.x, label.y, nameWidth, label.height), name.Truncate(nameWidth));
        if (!status.NullOrEmpty() && label.width - nameWidth > 30f * Scale)
        {
            GUI.color = bill.suspended ? Style.Accent : Style.Dim;
            Rect rest = new Rect(label.x + nameWidth + 8f * Scale, label.y, label.width - nameWidth - 8f * Scale, label.height);
            Widgets.Label(rest, status.Truncate(rest.width));
        }
        Text.WordWrap = true;
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        if (Mouse.IsOver(label))
        {
            TooltipHandler.TipRegion(label, name + (status.NullOrEmpty() ? "" : "\n\n" + status));
        }
    }

    // the surgeries the game offers for this pawn, as in its Add bill menu
    private static List<FloatMenuOption> SurgeryOptions(Pawn pawn)
    {
        List<FloatMenuOption> options = new();
        int index = 0;
        foreach (RecipeDef recipe in pawn.def.AllRecipes)
        {
            if (!recipe.AvailableNow)
            {
                continue;
            }
            AcceptanceReport report = recipe.Worker.AvailableReport(pawn);
            if (!report.Accepted && report.Reason.NullOrEmpty())
            {
                continue;
            }
            IEnumerable<ThingDef> missing = recipe.PotentiallyMissingIngredients(null, pawn.MapHeld);
            if (missing.Any(d => d.isTechHediff) || missing.Any(d => d.IsDrug) || (missing.Any() && recipe.dontShowIfAnyIngredientMissing))
            {
                continue;
            }
            if (recipe.targetsBodyPart)
            {
                foreach (BodyPartRecord part in recipe.Worker.GetPartsToApplyOn(pawn, recipe))
                {
                    if (recipe.AvailableOnNow(pawn, part))
                    {
                        options.Add(surgeryOption(pawn, pawn, recipe, missing, report, index++, part));
                    }
                }
            }
            else if (!pawn.health.hediffSet.HasHediff(recipe.addsHediff))
            {
                options.Add(surgeryOption(pawn, pawn, recipe, missing, report, index++, null));
            }
        }
        return options;
    }

    private static readonly Color Severe = new(0.92f, 0.45f, 0.45f);
    private static readonly Color Immune = new(0.55f, 0.85f, 0.6f);

    private static void ThinBar(Rect r, float value, Color color)
    {
        Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.08f));
        if (value > 0f)
        {
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, Mathf.Max(1f, Mathf.Round(r.width * value)), r.height), color);
        }
    }

    // right to left: info card, bleeding (bigger the faster), and the state icon (tended...); returns their width
    private static float Icons(Rect row, Condition c)
    {
        float size = Mathf.Round(16f * Scale);
        float x = row.xMax;
        Rect Next()
        {
            x -= size + 4f * Scale;
            return new Rect(x, Mathf.Round(row.center.y - size / 2f), size, size);
        }
        Widgets.InfoCardButton(Next(), c.Hediffs[0]);
        float bleeding = c.Hediffs.Sum(h => h.BleedRate);
        if (c.Hediffs.Any(h => h.Bleeding))
        {
            Rect r = Next();
            GUI.DrawTexture(r.ContractedBy(GenMath.LerpDouble(0f, 0.6f, 4f, 0f, Mathf.Min(bleeding, 1f)) * Scale), BleedingIcon);
        }
        TextureAndColor state = c.Hediffs[c.Hediffs.Count - 1].StateIcon;
        if (state.HasValue)
        {
            Rect r = Next();
            GUI.color = state.Color;
            GUI.DrawTexture(r, state.Texture);
            GUI.color = Color.white;
        }
        return row.xMax - x;
    }
}
