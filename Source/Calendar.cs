using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JustDesktop;

// a calendar for the quadrum (the game's month, 15 days) opened from the date: days gone by filled in, today in the
// accent, and days something happened (the letters still in the archive, and the ones kept in the save when that
// option is on) in that letter's color, with what happened on hover and a click to open it. The arrows go to other quadrums
public class Window_Calendar : Window
{
    private const float Cell = 34f;
    private const float Space = 4f;
    private const float Pad = 12f;
    private const int Columns = 5;

    private readonly Rect anchor;
    private int shown;

    // something that happened: a letter in the archive, or one kept in the save that the archive let go of
    private struct Event
    {
        public int Tick;
        public string Label;
        public LetterDef Def;
        public System.Action Open;
    }

    // events by day, made again when the archive or the kept ones change
    private static readonly Dictionary<int, List<Event>> events = new();
    private static int eventsCount = -1;
    private static int eventsTick = -1;

    // the order a day's color is picked in, worst first
    private static readonly List<LetterDef> severity = new();

    public static float Width => Pad * 2f + Columns * Cell + (Columns - 1) * Space;
    public static float Height => Pad * 2f + 26f + 18f + 8f + 3 * Cell + 2 * Space + 10f + 18f;

    public override Vector2 InitialSize => new(Width, Height);

    // the date's rect it opens above
    public Window_Calendar(Rect anchor)
    {
        this.anchor = anchor;
        doWindowBackground = false;
        drawShadow = false;
        closeOnClickedOutside = true;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
        layer = WindowLayer.GameUI;
        soundAppear = null;
        shown = Today / GenDate.DaysPerQuadrum;
    }

    public static void Toggle(Rect anchor)
    {
        if (Find.WindowStack.WindowOfType<Window_Calendar>() is { } open)
        {
            open.Close(false);
            return;
        }
        SoundDefOf.Tick_High.PlayOneShotOnCamera();
        Find.WindowStack.Add(new Window_Calendar(anchor));
    }

    protected override void SetInitialSizeAndPosition()
    {
        float x = Mathf.Clamp(anchor.center.x - Width / 2f, 6f, UI.screenWidth - Width - 6f);
        float y = Mathf.Max(6f, anchor.y - Height - 8f);
        windowRect = new Rect(x, y, Width, Height);
    }

    protected override float Margin => 0f;

    private static float Longitude
    {
        get
        {
            Map map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            return map != null ? Find.WorldGrid.LongLatOf(map.Tile).x : 0f;
        }
    }

    private static float Latitude
    {
        get
        {
            Map map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            return map != null ? Find.WorldGrid.LongLatOf(map.Tile).y : 0f;
        }
    }

    // days counted from the first day of 5500, so quadrums and years follow on
    public static int DayKey(long abs)
    {
        float lon = Longitude;
        return ((GenDate.Year(abs, lon) - 5500) * 4 + (int)GenDate.Quadrum(abs, lon)) * GenDate.DaysPerQuadrum + GenDate.DayOfQuadrum(abs, lon);
    }

    private static int Today => DayKey(GenTicks.TicksAbs);
    private static int FirstDay => DayKey(GenDate.TickGameToAbs(0));

    private static void Add(Event e)
    {
        int key = DayKey(GenDate.TickGameToAbs(e.Tick));
        if (!events.TryGetValue(key, out var list))
        {
            events[key] = list = new List<Event>();
        }
        list.Add(e);
    }

    private static readonly HashSet<(int, string)> inArchive = new();

    private static void Collect()
    {
        List<IArchivable> all = Find.Archive.ArchivablesListForReading;
        CalendarHistory history = CalendarHistory.On ? CalendarHistory.Get : null;
        // the option may have just been turned on (only does something once a session)
        history?.KeepArchive();
        int count = all.Count + (history?.All.Count ?? 0) * 10000;
        if (count == eventsCount && Find.TickManager.TicksGame - eventsTick < 2500)
        {
            return;
        }
        eventsCount = count;
        eventsTick = Find.TickManager.TicksGame;
        events.Clear();
        inArchive.Clear();
        foreach (IArchivable a in all)
        {
            if (a is not Letter letter)
            {
                continue;
            }
            inArchive.Add(CalendarHistory.Key(a.CreatedTicksGame, a.ArchivedLabel));
            Add(new Event { Tick = a.CreatedTicksGame, Label = a.ArchivedLabel, Def = letter.def, Open = a.OpenArchived });
        }
        // kept in the save: only the ones the archive let go of, opened as a plain message with their text
        foreach (CalendarHistory.Kept k in CalendarHistory.Get?.All ?? new List<CalendarHistory.Kept>())
        {
            if (!inArchive.Contains(CalendarHistory.Key(k.tick, k.label)))
            {
                CalendarHistory.Kept copy = k;
                Add(new Event { Tick = k.tick, Label = k.label, Def = k.def, Open = () => Find.WindowStack.Add(new Dialog_MessageBox(copy.text ?? "", title: copy.label)) });
            }
        }
        if (severity.Count == 0)
        {
            severity.AddRange(new[] { LetterDefOf.ThreatBig, LetterDefOf.Death, LetterDefOf.ThreatSmall, LetterDefOf.NegativeEvent, LetterDefOf.PositiveEvent, LetterDefOf.NeutralEvent }.Where(d => d != null));
        }
    }

    private static Color DayColor(List<Event> list)
    {
        foreach (LetterDef def in severity)
        {
            if (list.Any(e => e.Def == def))
            {
                return def.color;
            }
        }
        return list[0].Def.color;
    }

    public override void DoWindowContents(Rect inRect)
    {
        Collect();
        Pane.CardBack(inRect);
        Rect inner = inRect.ContractedBy(Pad);
        int today = Today, first = FirstDay;
        int year = 5500 + Mathf.FloorToInt(shown / 4f);
        Quadrum quadrum = (Quadrum)(((shown % 4) + 4) % 4);

        // the quadrum and year, with arrows to the ones before and after
        Rect prev = new Rect(inner.x, inner.y + 2f, 22f, 22f);
        Rect next = new Rect(inner.xMax - 22f, inner.y + 2f, 22f, 22f);
        bool canPrev = shown > first / GenDate.DaysPerQuadrum, canNext = shown < today / GenDate.DaysPerQuadrum;
        if (Arrow(prev, "<", canPrev))
        {
            shown--;
        }
        if (Arrow(next, ">", canNext))
        {
            shown++;
        }
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleCenter;
        GUI.color = Style.Text;
        Widgets.Label(new Rect(prev.xMax, inner.y, next.x - prev.xMax, 26f), quadrum.Label() + " " + year);
        Text.Font = GameFont.Tiny;
        GUI.color = Style.Dim;
        Widgets.Label(new Rect(inner.x, inner.y + 26f, inner.width, 18f), quadrum.GetSeason(Latitude).LabelCap());
        Text.Font = GameFont.Small;

        float top = inner.y + 26f + 18f + 8f;
        for (int d = 0; d < GenDate.DaysPerQuadrum; d++)
        {
            int key = shown * GenDate.DaysPerQuadrum + d;
            Rect r = new Rect(inner.x + d % Columns * (Cell + Space), top + d / Columns * (Cell + Space), Cell, Cell);
            DrawDay(r, d + 1, key, today, first);
        }

        // what the colors mean
        float ly = top + 3 * Cell + 2 * Space + 10f;
        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.MiddleLeft;
        float lx = inner.x;
        Legend(ref lx, ly, Style.Accent, "Today");
        Legend(ref lx, ly, new Color(1f, 1f, 1f, 0.3f), "Gone by");
        // events in a few of their colors
        foreach (LetterDef def in new[] { LetterDefOf.ThreatBig, LetterDefOf.ThreatSmall, LetterDefOf.PositiveEvent })
        {
            if (def != null)
            {
                GUI.color = def.color;
                Widgets.DrawAtlas(new Rect(lx, ly + 5f, 8f, 8f), Style.Round);
                lx += 10f;
            }
        }
        lx += 2f;
        GUI.color = Style.Dim;
        Widgets.Label(new Rect(lx, ly, Text.CalcSize("Events").x + 2f, 18f), "Events");
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
    }

    private static void Legend(ref float x, float y, Color color, string label)
    {
        GUI.color = color;
        Widgets.DrawAtlas(new Rect(x, y + 5f, 8f, 8f), Style.Round);
        GUI.color = Style.Dim;
        float w = Text.CalcSize(label).x;
        Widgets.Label(new Rect(x + 12f, y, w + 2f, 18f), label);
        x += 12f + w + 12f;
    }

    private static bool Arrow(Rect r, string label, bool enabled)
    {
        bool over = enabled && Mouse.IsOver(r);
        if (over)
        {
            GUI.color = Style.Hover;
            Widgets.DrawAtlas(r, Style.Round);
        }
        GUI.color = enabled ? over ? Color.white : Style.Text : Style.Disabled;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(r, label);
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        return enabled && Widgets.ButtonInvisible(r);
    }

    private static void DrawDay(Rect r, int number, int key, int today, int first)
    {
        bool before = key < first, past = key < today && !before, now = key == today;
        events.TryGetValue(key, out List<Event> list);
        bool any = list is { Count: > 0 };
        Color fill = now ? Style.Accent : any ? DayColor(list) : past ? new Color(1f, 1f, 1f, 0.16f) : new Color(1f, 1f, 1f, 0.04f);
        if (any && !now)
        {
            fill.a = 0.55f;
        }
        GUI.color = fill;
        Widgets.DrawAtlas(r, Style.Round);
        if (now && any)
        {
            // today with events: the event color as a dot
            GUI.color = DayColor(list);
            GUI.DrawTexture(new Rect(r.center.x - 3f, r.yMax - 8f, 6f, 6f), Style.Circle);
        }
        GUI.color = now || any ? Color.white : past ? Style.Text : before ? Style.Disabled : Style.Dim;
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(r, number.ToString());
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;

        if (Mouse.IsOver(r))
        {
            GUI.color = Style.Hover;
            Widgets.DrawAtlas(r, Style.Round);
            GUI.color = Color.white;
            string tip = number + " " + ((Quadrum)(((key / GenDate.DaysPerQuadrum) % 4 + 4) % 4)).Label();
            if (now)
            {
                tip += "  ·  today";
            }
            if (any)
            {
                tip += "\n";
                foreach (Event e in list.OrderBy(e => e.Tick))
                {
                    tip += "\n" + e.Label.Colorize(e.Def.color);
                }
                tip += "\n\n" + "Click to open one".Colorize(Style.Dim);
            }
            TooltipHandler.TipRegion(r, new TipSignal(tip, key ^ 0x3a51));
        }
        if (any && Widgets.ButtonInvisible(r))
        {
            if (list.Count == 1)
            {
                list[0].Open();
            }
            else
            {
                Find.WindowStack.Add(new FloatMenu(list.OrderBy(e => e.Tick).Select(e => new FloatMenuOption(e.Label, e.Open)).ToList()));
            }
        }
    }
}
