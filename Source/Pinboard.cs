using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using RimWorld.Planet;
using Verse.AI;
using Verse.Sound;

namespace JustDesktop;

// a small card you can move, with a row for each thing pinned to it: a pawn (health, mood, the temperature where
// they stand), a health condition (how it's going, bleeding, blood loss), a quest (time left to accept or finish)
// and a bill (who's working on it and how far the item is). As tall as its rows, hidden with nothing pinned.
// The pins are kept with the save, where the card sits only until the game closes... and in the mod settings
[StaticConstructorOnStartup]
public class Pinboard : GameComponent
{
    // everything pinned, in the order the rows show (dragging a row moves it here)
    private List<ILoadReferenceable> pins = new();

    public Pinboard(Game game)
    {
    }

    public static Pinboard Get => Current.Game?.GetComponent<Pinboard>();

    private static Texture2D _pin, _pinned;
    public static Texture2D PinTex => _pin ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Hud/Pin");
    public static Texture2D PinnedTex => _pinned ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Hud/Pinned");

    public override void ExposeData()
    {
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            pins.RemoveAll(x => x == null);
        }
        Scribe_Collections.Look(ref pins, "pins", LookMode.Reference);
        // the first version kept a list per kind, read those once into the one list
        if (Scribe.mode != LoadSaveMode.Saving)
        {
            Scribe_Collections.Look(ref oldPawns, "pawns", LookMode.Reference);
            Scribe_Collections.Look(ref oldHediffs, "hediffs", LookMode.Reference);
            Scribe_Collections.Look(ref oldQuests, "quests", LookMode.Reference);
            Scribe_Collections.Look(ref oldBills, "bills", LookMode.Reference);
        }
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            pins ??= new List<ILoadReferenceable>();
            foreach (System.Collections.IEnumerable old in new System.Collections.IEnumerable[] { oldPawns, oldHediffs, oldQuests, oldBills })
            {
                if (old != null)
                {
                    pins.AddRange(old.OfType<ILoadReferenceable>().Where(x => !pins.Contains(x)));
                }
            }
            oldPawns = null;
            oldHediffs = null;
            oldQuests = null;
            oldBills = null;
            pins.RemoveAll(x => x == null);
        }
    }

    private List<Pawn> oldPawns;
    private List<Hediff> oldHediffs;
    private List<Quest> oldQuests;
    private List<Bill> oldBills;

    // ---- pinning ----

    public static bool IsPinned(ILoadReferenceable thing) => Get is { } b && b.pins.Contains(thing);

    public static void Toggle(ILoadReferenceable thing)
    {
        Pinboard b = Get;
        if (b == null || thing == null)
        {
            return;
        }
        if (!b.pins.Remove(thing))
        {
            b.pins.Add(thing);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }
        else
        {
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }
        ended.Remove(thing);
    }

    // a pin button: the outline when not pinned, filled in the accent color when it is
    public static void Button(Rect r, ILoadReferenceable thing, string what)
    {
        bool pinned = IsPinned(thing);
        bool over = Mouse.IsOver(r);
        GUI.color = pinned ? Style.Accent : over ? Color.white : Style.Dim;
        GUI.DrawTexture(r, pinned ? PinnedTex : PinTex);
        GUI.color = Color.white;
        TooltipHandler.TipRegion(r, pinned ? "Pinned to the pinboard, click to take it off" : "Pin " + what + " to the pinboard, a small card you can move that keeps showing how it's going");
        if (Widgets.ButtonInvisible(r))
        {
            Toggle(thing);
        }
    }

    // ---- the card ----

    // a pin whose thing is gone or over keeps its row for a few seconds saying so, then goes
    private static readonly Dictionary<object, float> ended = new();
    private const float EndedSeconds = 8f;

    private static Settings S => JustDesktopMod.Settings;
    private static float Width => 270f;
    private const float Pad = 8f, Header = 20f, Row = 36f, Gap = 4f;
    private static bool dragging;
    private static Vector2 dragOffset;

    private struct Line
    {
        public string Title, Right, Sub;
        public Color TitleColor, RightColor;
        public float BarA, BarB;
        public Color ColorA, ColorB;
        public Action Click;
        public Action Unpin;
        public string Tip;
    }

    private readonly List<Line> lines = new();

    private void Collect()
    {
        lines.Clear();
        Tidy();
        foreach (ILoadReferenceable pin in pins)
        {
            lines.Add(pin switch
            {
                Pawn p => PawnLine(p),
                Hediff h => HediffLine(h),
                Quest q => QuestLine(q),
                Bill b => BillLine(b),
                Thing t => BenchLine(t),
                _ => new Line { Title = pin.ToString(), TitleColor = Style.Dim, BarA = -1f, BarB = -1f, Unpin = () => Toggle(pin) },
            });
        }
    }

    private static bool IsOver(ILoadReferenceable pin) => pin switch
    {
        Pawn p => p.Destroyed || p.Discarded,
        Hediff h => h.pawn == null || h.pawn.Dead || !h.pawn.health.hediffSet.hediffs.Contains(h),
        Quest q => q.State != QuestState.NotYetAccepted && q.State != QuestState.Ongoing,
        Bill b => b.DeletedOrDereferenced,
        Thing t => t.Destroyed,
        _ => false,
    };

    // over ones get a last row saying so, then come off
    private void Tidy()
    {
        for (int i = pins.Count - 1; i >= 0; i--)
        {
            ILoadReferenceable t = pins[i];
            if (!IsOver(t))
            {
                continue;
            }
            if (!ended.TryGetValue(t, out float since))
            {
                ended[t] = since = Time.realtimeSinceStartup;
            }
            if (Time.realtimeSinceStartup - since > EndedSeconds)
            {
                pins.RemoveAt(i);
                ended.Remove(t);
            }
        }
    }

    private bool Over(ILoadReferenceable o, out Line line, string text)
    {
        line = default;
        if (!ended.ContainsKey(o))
        {
            return false;
        }
        line = new Line { Title = text, TitleColor = Style.Dim, BarA = -1f, BarB = -1f, Unpin = () => Toggle(o) };
        return true;
    }

    private static string Outcome(Quest q) => q.State switch
    {
        QuestState.EndedSuccess => "done",
        QuestState.EndedFailed => "failed",
        QuestState.EndedOfferExpired => "offer ran out",
        _ => "over",
    };

    private static readonly Color HealthBar = new(0.38f, 0.75f, 0.44f), MoodBar = new(0.93f, 0.77f, 0.33f), Severe = new(0.85f, 0.35f, 0.3f), Immune = new(0.45f, 0.63f, 0.96f), Work = new(0.45f, 0.72f, 0.75f);
    private static readonly Color Cold = new(0.5f, 0.75f, 1f), Hot = new(1f, 0.5f, 0.4f);

    private Line PawnLine(Pawn p)
    {
        if (Over(p, out Line gone, p.LabelShortCap + ": gone"))
        {
            return gone;
        }
        Line l = new Line { Title = p.LabelShortCap, TitleColor = Style.Text, BarA = p.health.summaryHealth.SummaryHealthPercent, ColorA = HealthBar, BarB = p.needs?.mood?.CurLevelPercentage ?? -1f, ColorB = MoodBar, Click = () => CameraJumper.TryJumpAndSelect(p), Unpin = () => Toggle(p) };
        if (p.SpawnedOrAnyParentSpawned)
        {
            float t = p.AmbientTemperature;
            FloatRange comfort = p.ComfortableTemperatureRange();
            l.Right = t.ToStringTemperature("F0");
            l.RightColor = t < comfort.min ? Cold : t > comfort.max ? Hot : Style.Dim;
        }
        else
        {
            l.Right = p.IsCaravanMember() ? "in a caravan" : "away";
            l.RightColor = Style.Dim;
        }
        if (p.Downed)
        {
            l.Sub = "downed";
        }
        else if (p.InMentalState)
        {
            l.Sub = p.MentalStateDef.label;
        }
        l.Tip = "Health " + l.BarA.ToStringPercent() + (l.BarB >= 0f ? ", mood " + l.BarB.ToStringPercent() : "") + "\nThe temperature where they are, blue or red when it's outside what they find comfortable (" + p.ComfortableTemperatureRange().min.ToStringTemperature("F0") + " to " + p.ComfortableTemperatureRange().max.ToStringTemperature("F0") + ")";
        return l;
    }

    private Line HediffLine(Hediff h)
    {
        Pawn p = h.pawn;
        if (Over(h, out Line gone, (p?.LabelShortCap ?? "") + ": " + h.LabelCap + (p is { Dead: true } ? ", dead" : ", healed")))
        {
            return gone;
        }
        Line l = new Line { Title = p.LabelShortCap + ": " + h.LabelCap, TitleColor = h.LabelColor, BarA = -1f, BarB = -1f, Click = () => CameraJumper.TryJumpAndSelect(p), Unpin = () => Toggle(h), Tip = h.GetTooltip(p, false) };
        HediffComp_Immunizable immunizable = h.TryGetComp<HediffComp_Immunizable>();
        float deadly = h.def.lethalSeverity > 0f ? h.def.lethalSeverity : immunizable != null && h.def.maxSeverity < float.MaxValue ? h.def.maxSeverity : 0f;
        if (h.def == HediffDefOf.BloodLoss)
        {
            l.BarA = Mathf.Clamp01(h.Severity);
            l.ColorA = Severe;
            int ticks = HealthUtility.TicksUntilDeathDueToBloodLoss(p);
            l.Right = ticks < 60000 * 30 ? "death in " + ticks.ToStringTicksToPeriod(allowSeconds: false, shortForm: true) : h.Severity.ToStringPercent();
            l.RightColor = ticks < 60000 * 30 ? Severe : Style.Dim;
        }
        else if (deadly > 0f && h is not Hediff_Injury)
        {
            l.BarA = Mathf.Clamp01(h.Severity / deadly);
            l.ColorA = Severe;
            if (immunizable != null)
            {
                l.BarB = Mathf.Clamp01(immunizable.Immunity);
                l.ColorB = Immune;
                l.Right = l.BarB > l.BarA ? "winning" : "losing";
                l.RightColor = l.BarB > l.BarA ? Immune : Severe;
            }
            else
            {
                l.Right = l.BarA.ToStringPercent();
                l.RightColor = Style.Dim;
            }
        }
        if (h.Bleeding)
        {
            l.Right = "bleeding " + h.BleedRate.ToStringPercent() + "/day";
            l.RightColor = Severe;
        }
        if (h.TendableNow())
        {
            l.Sub = "needs tending";
        }
        else if (h is HediffWithComps && h.TryGetComp<HediffComp_TendDuration>() is { IsTended: true })
        {
            l.Sub = "tended";
        }
        return l;
    }

    private Line QuestLine(Quest q)
    {
        if (Over(q, out Line gone, q.name + ": " + Outcome(q)))
        {
            return gone;
        }
        Line l = new Line { Title = q.name, TitleColor = Style.Text, BarA = -1f, BarB = -1f, Unpin = () => Toggle(q), RightColor = Style.Dim, Click = () =>
        {
            Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Quests);
            ((MainTabWindow_Quests)MainButtonDefOf.Quests.TabWindow).Select(q);
        } };
        if (q.State == QuestState.NotYetAccepted)
        {
            l.Sub = "not accepted yet";
            if (q.TicksUntilExpiry >= 0)
            {
                l.Right = "accept in " + q.TicksUntilExpiry.ToStringTicksToPeriod(allowSeconds: false, shortForm: true);
                l.RightColor = Severe;
            }
        }
        else
        {
            QuestPart_Delay delay = q.PartsListForReading.OfType<QuestPart_Delay>().FirstOrDefault(d => d.State == QuestPartState.Enabled && d.isBad && !d.expiryInfoPart.NullOrEmpty());
            l.Sub = "accepted";
            if (delay != null)
            {
                l.Right = "ends in " + delay.TicksLeft.ToStringTicksToPeriod(allowSeconds: false, shortForm: true);
                l.RightColor = Severe;
                l.Tip = delay.expiryInfoPart;
            }
            else
            {
                l.Right = "no time limit";
            }
        }
        return l;
    }

    private static readonly Dictionary<JobDriver_DoBill, float> startWork = new();

    private Line BillLine(Bill b)
    {
        if (Over(b, out Line gone, b.LabelCap + ": gone"))
        {
            return gone;
        }
        Thing bench = b.billStack?.billGiver as Thing;
        Line l = new Line { Title = b.LabelCap, TitleColor = Style.Text, BarA = -1f, BarB = -1f, Unpin = () => Toggle(b), RightColor = Style.Dim, Click = () =>
        {
            if (bench != null)
            {
                CameraJumper.TryJumpAndSelect(bench);
            }
        } };
        if (b is Bill_Production prod)
        {
            l.Right = prod.RepeatInfoText;
        }
        float progress = -1f;
        bool at = false;
        string working = b.suspended || b is Bill_Production { paused: true } ? null : Working(bench, b, out progress, out at);
        if (b.suspended)
        {
            l.Sub = "paused";
        }
        else if (b is Bill_Production { paused: true })
        {
            l.Sub = "done for now, waiting for the count to drop";
        }
        else if (working == null)
        {
            l.Sub = "nobody on it";
        }
        else
        {
            l.Sub = working;
            if (at)
            {
                l.BarA = progress;
                l.ColorA = Work;
            }
        }
        if (bench != null)
        {
            l.Tip = bench.LabelCap;
        }
        return l;
    }

    // who's on a bill at that bench and how far the item is, null when nobody is
    private static string Working(Thing bench, Bill b, out float progress, out bool atBench)
    {
        progress = -1f;
        atBench = false;
        Pawn worker = bench?.MapHeld?.mapPawns.AllPawnsSpawned.FirstOrDefault(x => x.CurJob?.bill != null && (b == null ? x.CurJob.bill.billStack?.billGiver == bench : x.CurJob.bill == b));
        if (worker == null)
        {
            return null;
        }
        Bill bill = worker.CurJob.bill;
        if (worker.jobs.curDriver is JobDriver_DoBill driver && driver.workLeft > 0f && worker.Position == bench.InteractionCell)
        {
            if (!startWork.TryGetValue(driver, out float start) || driver.workLeft > start)
            {
                startWork[driver] = start = Mathf.Max(driver.workLeft, bill.recipe.WorkAmountTotal(null));
            }
            progress = Mathf.Clamp01(1f - driver.workLeft / start);
            atBench = true;
            return worker.LabelShortCap + (b == null ? ": " + bill.LabelCap : " making one") + ", " + progress.ToStringPercent();
        }
        return worker.LabelShortCap + (b == null ? " getting what " + bill.LabelCap + " needs" : " getting what it needs");
    }

    private Line BenchLine(Thing bench)
    {
        if (Over(bench, out Line gone, bench.LabelCap + ": gone"))
        {
            return gone;
        }
        Line l = new Line { Title = bench.LabelCap, TitleColor = Style.Text, BarA = -1f, BarB = -1f, RightColor = Style.Dim, Unpin = () => Toggle(bench), Click = () => CameraJumper.TryJumpAndSelect(bench) };
        BillStack stack = (bench as IBillGiver)?.BillStack;
        int count = stack?.Count ?? 0;
        int active = stack?.Bills.Count(x => x.ShouldDoNow()) ?? 0;
        l.Right = count == 0 ? "no bills" : count == 1 ? "1 bill" : count + " bills";
        string working = Working(bench, null, out float progress, out bool at);
        if (working != null)
        {
            l.Sub = working;
            if (at)
            {
                l.BarA = progress;
                l.ColorA = Work;
            }
        }
        else
        {
            l.Sub = count == 0 ? "nothing to make" : active == 0 ? "all bills paused or done" : "nobody on it";
        }
        if (stack != null && count > 0)
        {
            l.Tip = string.Join("\n", stack.Bills.Select(x => x.LabelCap + (x is Bill_Production pr ? "  " + pr.RepeatInfoText : "") + (x.suspended ? " (paused)" : "")));
        }
        return l;
    }

    private static float LineHeight(Line l) => 18f + (l.Sub != null || l.BarA >= 0f ? 14f : 0f);

    public static void OnGUI()
    {
        Pinboard b = Get;
        if (b == null || Current.ProgramState != ProgramState.Playing || Find.UIRoot.screenshotMode.FiltersCurrentEvent)
        {
            return;
        }
        if (b.pins.Count == 0)
        {
            dragging = false;
            pressed = -1;
            return;
        }
        b.DragRows();
        b.Collect();
        float h = Header + (S.pinboardFolded ? 0f : b.lines.Sum(l => LineHeight(l) + Gap) + Pad - Gap + 2f);
        Vector2 pos = S.pinboardPos.x < 0f ? new Vector2(UI.screenWidth - Width - 12f, 110f) : S.pinboardPos;
        if (dragging)
        {
            if (Input.GetMouseButton(0))
            {
                pos = UI.MousePositionOnUIInverted - dragOffset;
            }
            else
            {
                dragging = false;
            }
        }
        pos.x = Mathf.Clamp(pos.x, 0f, UI.screenWidth - Width);
        pos.y = Mathf.Clamp(pos.y, 0f, UI.screenHeight - Header);
        S.pinboardPos = pos;
        Rect rect = new Rect(pos.x, pos.y, Width, h);
        Find.WindowStack.ImmediateWindow(71538604, rect, WindowLayer.GameUI, () => b.Draw(rect.AtZero()), doBackground: false, absorbInputAroundWindow: false, shadowAlpha: 0f);
    }

    private void Draw(Rect all)
    {
        Pane.CardBack(all);
        // the header: drag it to move the card, the arrow folds it
        Rect header = new Rect(all.x, all.y, all.width, Header);
        Rect fold = new Rect(header.xMax - Header - 2f, header.y, Header, Header);
        GUI.color = Style.Dim;
        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(header.x + Pad, header.y, header.width - Pad * 2f - Header, Header), "Pinned");
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(fold, S.pinboardFolded ? "▸" : "▾");
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        TooltipHandler.TipRegion(fold, S.pinboardFolded ? "Show the pins" : "Fold it to this bar");
        if (Widgets.ButtonInvisible(fold))
        {
            S.pinboardFolded = !S.pinboardFolded;
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }
        else if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && Mouse.IsOver(header))
        {
            dragging = true;
            dragOffset = UI.MousePositionOnUIInverted - S.pinboardPos;
            Event.current.Use();
        }
        if (S.pinboardFolded)
        {
            Text.Font = GameFont.Small;
            return;
        }
        float y = Header;
        rowTops.Clear();
        for (int i = 0; i < lines.Count; i++)
        {
            Line l = lines[i];
            float lh = LineHeight(l);
            Rect row = new Rect(all.x + Pad, y, all.width - Pad * 2f, lh);
            rowTops.Add(S.pinboardPos.y + y + lh / 2f);
            DrawLine(row, l, i);
            // the row being dragged fades while it moves
            if (moving && i == pressed)
            {
                Widgets.DrawBoxSolid(row.ExpandedBy(3f, 1f), new Color(0.08f, 0.09f, 0.1f, 0.65f));
            }
            y += lh + Gap;
        }
        // where a dragged row would land
        if (moving)
        {
            int to = DropIndex();
            float lineY = Header - Gap / 2f;
            for (int i = 0; i < to && i < lines.Count; i++)
            {
                lineY += LineHeight(lines[i]) + Gap;
            }
            GUI.color = Style.Accent;
            GUI.DrawTexture(new Rect(all.x + Pad, Mathf.Round(lineY - 1f), all.width - Pad * 2f, 2f), BaseContent.WhiteTex);
            GUI.color = Color.white;
        }
        Text.Font = GameFont.Small;
    }

    // ---- dragging a row to another place ----

    // the row pressed with the left button; a press that moves becomes a drag, one that doesn't is a click
    private static int pressed = -1;
    private static Vector2 pressAt;
    private static bool moving;
    private static readonly List<float> rowTops = new();

    // the slot under the mouse, between rows: 0 = above the first
    private static int DropIndex()
    {
        float y = UI.MousePositionOnUIInverted.y;
        int to = 0;
        while (to < rowTops.Count && y > rowTops[to])
        {
            to++;
        }
        return to;
    }

    private void DragRows()
    {
        if (pressed < 0)
        {
            return;
        }
        if (Input.GetMouseButton(0))
        {
            if (!moving && (UI.MousePositionOnUIInverted - pressAt).magnitude > 5f)
            {
                moving = true;
            }
            return;
        }
        // let go: put it in its new place, or it was a click
        if (moving)
        {
            int to = DropIndex();
            if (pressed < pins.Count && to != pressed && to != pressed + 1)
            {
                ILoadReferenceable pin = pins[pressed];
                pins.RemoveAt(pressed);
                pins.Insert(to > pressed ? to - 1 : to, pin);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
        }
        else if (pressed < lines.Count)
        {
            lines[pressed].Click?.Invoke();
        }
        pressed = -1;
        moving = false;
    }

    private static void DrawLine(Rect row, Line l, int index)
    {
        bool over = Mouse.IsOver(row) && !moving;
        if (over)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.05f);
            Widgets.DrawAtlas(row.ExpandedBy(3f, 1f), ModernWidgets.Fill);
            GUI.color = Color.white;
        }
        Text.Font = GameFont.Small;
        Text.WordWrap = false;
        float rightW = l.Right.NullOrEmpty() ? 0f : Mathf.Min(Text.CalcSize(l.Right).x, row.width * 0.5f);
        Text.Anchor = TextAnchor.MiddleLeft;
        GUI.color = l.TitleColor;
        Rect title = new Rect(row.x, row.y, row.width - rightW - 6f, 18f);
        Widgets.Label(title, l.Title.Truncate(title.width));
        if (!l.Right.NullOrEmpty())
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = l.RightColor;
            Widgets.Label(new Rect(row.xMax - rightW - 4f, row.y, rightW + 4f, 18f), l.Right.Truncate(rightW + 4f));
        }
        Text.Font = GameFont.Tiny;
        float y = row.y + 19f;
        if (l.BarA >= 0f)
        {
            float barW = l.BarB >= 0f ? Mathf.Floor((row.width - 6f) / 2f) : l.Sub != null ? Mathf.Round(row.width * 0.45f) : row.width;
            ThinBar(new Rect(row.x, y + 4f, barW, 5f), l.BarA, l.ColorA);
            float x = row.x + barW + 6f;
            if (l.BarB >= 0f)
            {
                ThinBar(new Rect(x, y + 4f, barW, 5f), l.BarB, l.ColorB);
            }
            else if (l.Sub != null)
            {
                GUI.color = Style.Dim;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(x, y, row.xMax - x, 13f), l.Sub.Truncate(row.xMax - x));
            }
        }
        else if (l.Sub != null)
        {
            GUI.color = Style.Dim;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(row.x, y, row.width, 13f), l.Sub.Truncate(row.width));
        }
        Text.Anchor = TextAnchor.UpperLeft;
        Text.WordWrap = true;
        Text.Font = GameFont.Small;
        GUI.color = Color.white;
        if (over)
        {
            TooltipHandler.TipRegion(row, (l.Tip.NullOrEmpty() ? "" : l.Tip + "\n\n") + "Click to go there, drag to move it, right click to unpin");
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
            {
                l.Unpin?.Invoke();
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                pressed = index;
                pressAt = UI.MousePositionOnUIInverted;
                moving = false;
                Event.current.Use();
            }
        }
    }

    private static void ThinBar(Rect r, float value, Color color)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.45f);
        Widgets.DrawAtlas(r, ModernWidgets.Fill);
        float w = Mathf.Round(r.width * Mathf.Clamp01(value));
        if (w >= 2f)
        {
            GUI.color = new Color(color.r, color.g, color.b, 0.85f);
            Widgets.DrawAtlas(new Rect(r.x, r.y, w, r.height), ModernWidgets.Fill);
        }
        GUI.color = Color.white;
    }
}

[HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
public static class Pinboard_OnGUI
{
    public static void Postfix() => Pinboard.OnGUI();
}

// the pin button next to the dismiss one at the top right of a quest
[HarmonyPatch(typeof(MainTabWindow_Quests), "DoDismissButton")]
public static class Pinboard_QuestButton
{
    private static readonly AccessTools.FieldRef<MainTabWindow_Quests, Quest> selected = AccessTools.FieldRefAccess<MainTabWindow_Quests, Quest>("selected");

    public static void Postfix(MainTabWindow_Quests __instance, Rect innerRect)
    {
        Quest q = selected(__instance);
        if (q != null && !q.Historical)
        {
            Pinboard.Button(new Rect(innerRect.xMax - 62f, innerRect.y + 4f, 24f, 24f), q, "this quest");
        }
    }
}

// on each bill, left of its pause button
[HarmonyPatch(typeof(Bill), nameof(Bill.DoInterface))]
public static class Pinboard_BillButton
{
    private static readonly Func<Bill, bool> canCopy = AccessTools.MethodDelegate<Func<Bill, bool>>(AccessTools.PropertyGetter(typeof(Bill), "CanCopy"));

    public static void Postfix(Bill __instance, float x, float y, float width)
    {
        if (__instance.billStack?.billGiver is not Thing)
        {
            return;
        }
        float left = width - 24f - (canCopy(__instance) ? 28f : 0f) - 24f;
        Pinboard.Button(new Rect(x + left - 26f, y + 2f, 20f, 20f), __instance, "this bill");
    }
}

// the workbench itself, next to its Add bill button
[HarmonyPatch(typeof(BillStack), nameof(BillStack.DoListing))]
public static class Pinboard_BenchButton
{
    public static void Postfix(BillStack __instance, Rect rect)
    {
        if (__instance.billGiver is Thing bench && Current.ProgramState == ProgramState.Playing)
        {
            Pinboard.Button(new Rect(rect.x + 158f, rect.y + 4f, 22f, 22f), bench, bench.LabelShortCap + " (which bill is being made, by whom and how far)");
        }
    }
}
