using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JustDesktop;

// the History graph in the modern windows: the graphs and the time ranges as pills, a legend of pills with each
// line's latest value that hide or show it, and the graph on a rounded card: faint grid, dim axis labels, 2px lines
// with a soft fill under them, the colony's events as dots along the top, and on hover every line's value that day
public static class HistoryGraph
{
    private static readonly AccessTools.FieldRef<MainTabWindow_History, HistoryAutoRecorderGroup> group = AccessTools.FieldRefAccess<MainTabWindow_History, HistoryAutoRecorderGroup>("historyAutoRecorderGroup");
    private static readonly AccessTools.FieldRef<MainTabWindow_History, FloatRange> section = AccessTools.FieldRefAccess<MainTabWindow_History, FloatRange>("graphSection");

    // lines hidden from the legend, until the game closes
    private static readonly HashSet<HistoryAutoRecorderDef> hidden = new();

    private const float Pill = 26f;
    private const float AxisLeft = 52f;
    private const float AxisBottom = 20f;
    private const float MarkStrip = 14f;

    // the recorded values as curves by day, made again when the game moves on
    private static readonly Dictionary<HistoryAutoRecorder, SimpleCurve> curves = new();
    private static int curvesTick = -1;
    private static HistoryAutoRecorderGroup curvesGroup;

    private static SimpleCurve CurveFor(HistoryAutoRecorder recorder)
    {
        if (!curves.TryGetValue(recorder, out SimpleCurve curve))
        {
            curve = new SimpleCurve();
            for (int i = 0; i < recorder.records.Count; i++)
            {
                curve.Add(new CurvePoint(i * (float)recorder.def.recordTicksFrequency / 60000f, recorder.records[i]), false);
            }
            curve.SortPoints();
            curves[recorder] = curve;
        }
        return curve;
    }

    public static void Draw(MainTabWindow_History window, Rect rect)
    {
        rect.yMin += 17f;
        HistoryAutoRecorderGroup g = group(window);
        int tick = Find.TickManager.TicksGame;
        if (tick != curvesTick || g != curvesGroup)
        {
            curves.Clear();
            curvesTick = tick;
            curvesGroup = g;
        }
        Text.Font = GameFont.Small;
        float y = rect.y + 4f;
        y = Graphs(new Rect(rect.x, y, rect.width - 4f * 110f - 20f, Pill), window, g);
        Ranges(new Rect(rect.xMax - 4f * 110f, rect.y + 4f, 4f * 110f, Pill), window);
        if (g == null)
        {
            return;
        }
        y = Legend(new Rect(rect.x, y + 8f, rect.width, Pill), g) + 8f;
        Rect card = new Rect(rect.x, y, rect.width, rect.yMax - y - 4f);
        GUI.color = new Color(1f, 1f, 1f, 0.03f);
        Widgets.DrawAtlas(card, ModernWidgets.Fill);
        GUI.color = Style.Border;
        Widgets.DrawAtlas(card, ModernWidgets.Line);
        GUI.color = Color.white;
        Graph(card.ContractedBy(10f), window, g);
    }

    private static bool PillButton(Rect r, string label, bool on, Color? dot = null, bool dim = false)
    {
        bool over = Mouse.IsOver(r);
        GUI.color = on ? Style.Faded(0.3f) : over ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.06f);
        Widgets.DrawAtlas(r, ModernWidgets.Fill);
        float x = r.x + 10f;
        if (dot is { } c)
        {
            GUI.color = dim ? new Color(c.r, c.g, c.b, 0.3f) : c;
            GUI.DrawTexture(new Rect(x, Mathf.Round(r.center.y - 5f), 10f, 10f), Style.Circle);
            x += 16f;
        }
        GUI.color = dim ? Style.Disabled : on || over ? Color.white : Style.Text;
        Text.Anchor = TextAnchor.MiddleLeft;
        Text.WordWrap = false;
        Widgets.Label(new Rect(x, r.y, r.xMax - x - 6f, r.height), label);
        Text.WordWrap = true;
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        return Widgets.ButtonInvisible(r);
    }

    private static float PillWidth(string label, bool dot) => Mathf.Ceil(Text.CalcSize(label).x) + 20f + (dot ? 16f : 0f);

    // every graph the game records as a pill, wrapping onto more lines when there are many; returns where they end
    private static float Graphs(Rect row, MainTabWindow_History window, HistoryAutoRecorderGroup current)
    {
        float x = row.x, y = row.y;
        foreach (HistoryAutoRecorderGroup g in Find.History.Groups())
        {
            if (g.def.devModeOnly && !Prefs.DevMode)
            {
                continue;
            }
            string label = g.def.LabelCap;
            float w = PillWidth(label, false);
            if (x > row.x && x + w > row.xMax)
            {
                x = row.x;
                y += Pill + 4f;
            }
            if (PillButton(new Rect(x, y, w, Pill), label, g == current) && g != current)
            {
                group(window) = g;
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.HistoryTab, KnowledgeAmount.Total);
            }
            x += w + 4f;
        }
        return y + Pill;
    }

    // the last 30, 100 and 300 days, and all of it
    private static void Ranges(Rect row, MainTabWindow_History window)
    {
        float now = Find.TickManager.TicksGame / 60000f;
        FloatRange current = section(window);
        (string label, float days)[] ranges = { ("Last30Days".Translate(), 30f), ("Last100Days".Translate(), 100f), ("Last300Days".Translate(), 300f), ("AllDays".Translate(), -1f) };
        float x = row.xMax;
        for (int i = ranges.Length - 1; i >= 0; i--)
        {
            float w = Mathf.Min(PillWidth(ranges[i].label, false), 106f);
            x -= w;
            FloatRange range = ranges[i].days < 0f ? new FloatRange(0f, now) : new FloatRange(Mathf.Max(0f, now - ranges[i].days), now);
            bool on = Mathf.Abs(current.min - range.min) < 0.5f && Mathf.Abs(current.max - range.max) < 0.5f;
            if (PillButton(new Rect(x, row.y, w, Pill), ranges[i].label, on))
            {
                section(window) = range;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            x -= 4f;
        }
    }

    // each line: its color, name and latest value; a click hides or shows it
    private static float Legend(Rect row, HistoryAutoRecorderGroup g)
    {
        float x = row.x, y = row.y;
        foreach (HistoryAutoRecorder recorder in g.recorders)
        {
            bool off = hidden.Contains(recorder.def);
            string label = recorder.def.LabelCap + (recorder.records.Count > 0 ? "   " + Value(recorder.def, recorder.records[recorder.records.Count - 1]) : "");
            float w = PillWidth(label, true);
            if (x > row.x && x + w > row.xMax)
            {
                x = row.x;
                y += Pill + 4f;
            }
            Rect r = new Rect(x, y, w, Pill);
            if (PillButton(r, label, false, recorder.def.graphColor, off))
            {
                if (!hidden.Remove(recorder.def))
                {
                    hidden.Add(recorder.def);
                }
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(r, off ? "Click to show this line" : "Click to hide this line");
            x += w + 4f;
        }
        return y + Pill;
    }

    private static string Value(HistoryAutoRecorderDef def, float v)
    {
        string number = Mathf.Abs(v) >= 1000f ? Compact(v) : v.ToString("0.##");
        return def.valueFormat.NullOrEmpty() ? number : string.Format(def.valueFormat, number);
    }

    // 12.3K, 1.2M
    private static string Compact(float v)
    {
        float a = Mathf.Abs(v);
        return a >= 1e6f ? (v / 1e6f).ToString("0.#") + "M" : a >= 1e3f ? (v / 1e3f).ToString("0.#") + "K" : v.ToString("0.##");
    }

    // 1, 2 or 5 times a power of ten, so the grid lands on round numbers
    private static float NiceStep(float range, int lines)
    {
        float raw = range / Mathf.Max(1, lines);
        if (raw <= 0f)
        {
            return 1f;
        }
        float power = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(raw)));
        float f = raw / power;
        return (f <= 1f ? 1f : f <= 2f ? 2f : f <= 5f ? 5f : 10f) * power;
    }

    private static void Graph(Rect rect, MainTabWindow_History window, HistoryAutoRecorderGroup g)
    {
        FloatRange days = section(window);
        if (days.max - days.min < 0.01f)
        {
            days.max = days.min + 0.01f;
        }
        List<HistoryAutoRecorder> shown = g.recorders.Where(r => !hidden.Contains(r.def) && r.records.Count > 0).ToList();

        // the values in view decide the scale, unless the graph has a fixed one
        float low = float.MaxValue, high = float.MinValue;
        foreach (HistoryAutoRecorder recorder in shown)
        {
            SimpleCurve curve = CurveFor(recorder);
            foreach (CurvePoint point in curve.Points)
            {
                if (point.x >= days.min && point.x <= days.max)
                {
                    low = Mathf.Min(low, point.y);
                    high = Mathf.Max(high, point.y);
                }
            }
            low = Mathf.Min(low, curve.Evaluate(days.min), curve.Evaluate(days.max));
            high = Mathf.Max(high, curve.Evaluate(days.min), curve.Evaluate(days.max));
        }
        if (low > high)
        {
            low = 0f;
            high = 1f;
        }
        if (g.def.useFixedScale)
        {
            low = g.def.fixedScale.x;
            high = g.def.fixedScale.y;
        }
        else
        {
            if (g.def.onlyPositiveValues)
            {
                low = Mathf.Min(0f, low);
            }
            float step0 = NiceStep(Mathf.Max(high - low, 1f), 4);
            low = Mathf.Floor(low / step0) * step0;
            high = Mathf.Max(Mathf.Ceil(high / step0) * step0, low + step0);
        }

        Rect marks = new Rect(rect.x + AxisLeft, rect.y, rect.width - AxisLeft, MarkStrip);
        Rect plot = new Rect(rect.x + AxisLeft, rect.y + MarkStrip + 4f, rect.width - AxisLeft, rect.height - MarkStrip - 4f - AxisBottom);
        float X(float day) => plot.x + plot.width * Mathf.InverseLerp(days.min, days.max, day);
        float Y(float v) => plot.yMax - plot.height * Mathf.InverseLerp(low, high, v);

        // grid and labels
        Text.Font = GameFont.Tiny;
        float step = NiceStep(high - low, 4);
        for (float v = low; v <= high + step * 0.01f; v += step)
        {
            float gy = Mathf.Round(Y(v));
            GUI.color = new Color(1f, 1f, 1f, v == 0f ? 0.14f : 0.06f);
            GUI.DrawTexture(new Rect(plot.x, gy, plot.width, 1f), BaseContent.WhiteTex);
            GUI.color = Style.Dim;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(rect.x, gy - 10f, AxisLeft - 8f, 20f), g.def.integersOnly || Mathf.Abs(v) >= 1000f ? Compact(Mathf.Round(v)) : v.ToString("0.##"));
        }
        float dayStep = Mathf.Max(1f, NiceStep(days.max - days.min, 6));
        for (float d = Mathf.Ceil(days.min / dayStep) * dayStep; d <= days.max; d += dayStep)
        {
            float gx = Mathf.Round(X(d));
            GUI.color = new Color(1f, 1f, 1f, 0.04f);
            GUI.DrawTexture(new Rect(gx, plot.y, 1f, plot.height), BaseContent.WhiteTex);
            GUI.color = Style.Dim;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(gx - 30f, plot.yMax + 3f, 60f, AxisBottom), "Day".Translate() + " " + d.ToString("0"));
        }
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;

        // the colony's events: dots along the top, a faint line down through the graph
        foreach (Tale tale in Find.TaleManager.AllTalesListForReading)
        {
            if (tale.def.type != TaleType.PermanentHistorical || tale.hidden)
            {
                continue;
            }
            float day = GenDate.TickAbsToGame(tale.date) / 60000f;
            if (day < days.min || day > days.max)
            {
                continue;
            }
            float mx = Mathf.Round(X(day));
            Color c = tale.def.historyGraphColor;
            GUI.color = new Color(c.r, c.g, c.b, 0.12f);
            GUI.DrawTexture(new Rect(mx, plot.y, 1f, plot.height), BaseContent.WhiteTex);
            GUI.color = c;
            Rect dot = new Rect(mx - 4f, marks.center.y - 4f, 8f, 8f);
            GUI.DrawTexture(dot, Style.Circle);
            if (Mouse.IsOver(dot.ExpandedBy(3f)))
            {
                TooltipHandler.TipRegion(dot.ExpandedBy(3f), new TipSignal(tale.ShortSummary));
            }
        }
        GUI.color = Color.white;

        // the lines: a soft fill under each, then the line, sampled every two pixels
        if (Event.current.type == EventType.Repaint)
        {
            foreach (HistoryAutoRecorder recorder in shown)
            {
                SimpleCurve curve = CurveFor(recorder);
                Color c = recorder.def.graphColor;
                GUI.color = new Color(c.r, c.g, c.b, 0.08f);
                for (float px = 0f; px < plot.width; px += 2f)
                {
                    float top = Mathf.Clamp(Y(curve.Evaluate(Mathf.Lerp(days.min, days.max, px / plot.width))), plot.y, plot.yMax);
                    GUI.DrawTexture(new Rect(plot.x + px, top, 2f, plot.yMax - top), BaseContent.WhiteTex);
                }
                GUI.color = Color.white;
                Vector2 last = default;
                for (float px = 0f; px <= plot.width; px += 2f)
                {
                    float day = Mathf.Lerp(days.min, days.max, px / plot.width);
                    Vector2 point = new Vector2(plot.x + px, Mathf.Clamp(Y(curve.Evaluate(day)), plot.y, plot.yMax));
                    if (px > 0f)
                    {
                        Widgets.DrawLine(last, point, c, 2f);
                    }
                    last = point;
                }
            }
        }

        Hover(plot, days, shown, Y);
    }

    // a line where the mouse is, a dot on each line and a small card with every value on that day
    private static void Hover(Rect plot, FloatRange days, List<HistoryAutoRecorder> shown, System.Func<float, float> Y)
    {
        if (!Mouse.IsOver(plot) || shown.Count == 0)
        {
            return;
        }
        float mx = Event.current.mousePosition.x;
        float day = Mathf.Lerp(days.min, days.max, Mathf.InverseLerp(plot.x, plot.xMax, mx));
        GUI.color = new Color(1f, 1f, 1f, 0.25f);
        GUI.DrawTexture(new Rect(Mathf.Round(mx), plot.y, 1f, plot.height), BaseContent.WhiteTex);
        foreach (HistoryAutoRecorder recorder in shown)
        {
            float v = CurveFor(recorder).Evaluate(day);
            GUI.color = recorder.def.graphColor;
            GUI.DrawTexture(new Rect(mx - 4f, Mathf.Clamp(Y(v), plot.y, plot.yMax) - 4f, 8f, 8f), Style.Circle);
        }
        Text.Font = GameFont.Small;
        string title = "Day".Translate() + " " + day.ToString("0.#");
        float w = Text.CalcSize(title).x;
        foreach (HistoryAutoRecorder recorder in shown)
        {
            w = Mathf.Max(w, Text.CalcSize(recorder.def.LabelCap + "    " + Value(recorder.def, CurveFor(recorder).Evaluate(day))).x + 18f);
        }
        w += 20f;
        float h = 10f + 22f * (shown.Count + 1);
        Rect card = new Rect(mx + 14f, Event.current.mousePosition.y + 10f, w, h);
        if (card.xMax > plot.xMax)
        {
            card.x = mx - 14f - w;
        }
        card.y = Mathf.Clamp(card.y, plot.y, plot.yMax - h);
        GUI.color = JustDesktopMod.Settings.windowColor;
        Widgets.DrawAtlas(card, ModernWidgets.Fill);
        GUI.color = Style.Border;
        Widgets.DrawAtlas(card, ModernWidgets.Line);
        GUI.color = Style.Dim;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(card.x + 10f, card.y + 5f, w - 20f, 22f), title);
        float y = card.y + 27f;
        foreach (HistoryAutoRecorder recorder in shown)
        {
            GUI.color = recorder.def.graphColor;
            GUI.DrawTexture(new Rect(card.x + 10f, y + 6f, 10f, 10f), Style.Circle);
            GUI.color = Style.Text;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(card.x + 26f, y, w - 36f, 22f), recorder.def.LabelCap);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(card.x + 26f, y, w - 36f, 22f), Value(recorder.def, CurveFor(recorder).Evaluate(day)));
            y += 22f;
        }
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
    }
}

[HarmonyPatch(typeof(MainTabWindow_History), "DoGraphPage")]
public static class HistoryGraph_Page
{
    public static bool Prefix(MainTabWindow_History __instance, Rect rect)
    {
        if (!ModernWidgets.Active)
        {
            return true;
        }
        HistoryGraph.Draw(__instance, rect);
        return false;
    }
}
