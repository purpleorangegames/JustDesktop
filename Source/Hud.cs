using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JustDesktop;

// what stays on screen around the map: the toggles, game speed, date, weather and temperature at the bottom right,
// letters and alerts above them, resources at the top left, messages, and what's under the mouse at the bottom left.
// Each on a rounded card; with the dock in the middle the bottom right ones can sit next to it instead, like an OS
// tray: toggles, speed and date at its right, weather and temperature at its left
public static class Hud
{
    private const float Pad = 8f;
    private const float Gap = 10f;
    private const float Step = WidgetRow.IconSize + WidgetRow.DefaultGap;

    private static Settings S => JustDesktopMod.Settings;
    public static bool Cards => S.hudCards && Loading.Ready;
    public static bool Beside => Cards && S.hudBesideDock && S.style == BarStyle.Dock && S.align == BarAlign.Center && Dock.Rest.width > 0f;
    public static Color Back => new(S.windowColor.r, S.windowColor.g, S.windowColor.b, S.hudOpacity);

    // while the toggles are drawn, so their check marks become our look
    public static bool InTray;

    private static int toggleCount = 12;
    private static int toggleRows = 3;

    private static readonly System.Func<string> temperatureString = AccessTools.MethodDelegate<System.Func<string>>(AccessTools.Method(typeof(GlobalControls), "TemperatureString"));
    private static readonly System.Action<Rect, TimedDetectionRaids> countdown = AccessTools.MethodDelegate<System.Action<Rect, TimedDetectionRaids>>(AccessTools.Method(typeof(GlobalControls), "DoCountdownTimer"));
    private static readonly List<string> hours24 = AccessTools.StaticFieldRefAccess<List<string>>(typeof(DateReadout), "fastHourStrings24h");
    private static readonly List<string> hours12 = AccessTools.StaticFieldRefAccess<List<string>>(typeof(DateReadout), "fastHourStrings12h");

    public static void Card(Rect rect)
    {
        GUI.color = Back;
        Widgets.DrawAtlas(rect, Style.Round);
        GUI.color = Style.Border;
        Widgets.DrawAtlas(rect, Style.RoundLine);
        GUI.color = Color.white;
    }

    // the game's GlobalControlsOnGUI with the cards, and the tray next to the dock
    public static void Controls(WidgetRow row, bool world)
    {
        Map map = Find.CurrentMap;
        float cardRight = UI.screenWidth - 6f;
        float right = cardRight - Pad;
        float y;
        if (SpeedAbove)
        {
            SpeedAboveBar(world);
        }
        Rect? tray = Beside ? Tray(row, world) : null;
        TrayShown = tray != null;
        if (tray is { } t)
        {
            if (!world)
            {
                Weather(map);
            }
            // the corner is free when the tray doesn't reach it
            y = t.xMax < UI.screenWidth - 170f ? UI.screenHeight - 8f : UI.screenHeight - Taskbar.Reserved - 4f;
        }
        else
        {
            WeatherCard = default;
            y = UI.screenHeight - Taskbar.Reserved - 6f;
            y = CornerToggles(row, cardRight, y, world) - 6f;
            y = CornerClock(map, cardRight, right, y, world);
        }

        // the rest as the game has it, lined up with the cards
        float x = right - 193f;
        float conditionsWidth = 154f;
        GameConditionManager manager = world ? Find.World.gameConditionManager : map.gameConditionManager;
        float conditionsHeight = manager.TotalHeightAt(conditionsWidth);
        Rect conditions = new Rect(cardRight + 6f - conditionsWidth, y - conditionsHeight, conditionsWidth, conditionsHeight);
        manager.DoConditionsUI(conditions);
        y -= conditions.height;
        if (DebugViewSettings.showMemoryInfo)
        {
            GlobalControlsUtility.DrawMemoryInfo(x, 200f, ref y);
        }
        if (DebugViewSettings.showTpsCounter)
        {
            GlobalControlsUtility.DrawTpsCounter(x, 200f, ref y);
        }
        if (DebugViewSettings.showFpsCounter)
        {
            GlobalControlsUtility.DrawFpsCounter(x, 200f, ref y);
        }
        if (Prefs.ShowRealtimeClock)
        {
            GlobalControlsUtility.DoRealtimeClock(x, 200f, ref y);
        }
        if (world)
        {
            if (!Find.WorldTargeter.IsTargeting)
            {
                Find.WorldRoutePlanner.DoRoutePlannerButton(ref y);
            }
            if (!Find.PlaySettings.lockNorthUp)
            {
                CompassWidget.CompassOnGUI(ref y);
            }
            y -= 10f;
            y = Notifications.DrawBell(y);
            Find.LetterStack.LettersOnGUI(y);
            return;
        }
        TimedDetectionRaids raids = map.Parent?.GetComponent<TimedDetectionRaids>();
        if (raids != null && raids.NextRaidCountdownActiveAndVisible)
        {
            Rect r = new Rect(x, y - 26f, 193f, 26f);
            Text.Anchor = TextAnchor.UpperRight;
            countdown(r, raids);
            Text.Anchor = TextAnchor.UpperLeft;
            y -= 26f;
        }
        y -= 10f;
        y = Notifications.DrawBell(y);
        Find.LetterStack.LettersOnGUI(y);
    }

    // the toggles five to a row going up, on a card, at the bottom right
    private static float CornerToggles(WidgetRow row, float cardRight, float y, bool world)
    {
        int cols = Mathf.Clamp(toggleCount, 1, 5);
        float width = cols * Step - WidgetRow.DefaultGap;
        Rect card = new Rect(cardRight - width - Pad * 2f, y - toggleRows * Step + WidgetRow.DefaultGap - Pad * 2f, width + Pad * 2f, toggleRows * Step - WidgetRow.DefaultGap + Pad * 2f);
        Card(card);
        float startX = cardRight - Pad, startY = y - Pad - WidgetRow.IconSize;
        row.Init(startX, startY, UIDirection.LeftThenUp, 141f);
        DrawToggles(row, world);
        bool partial = row.FinalX != startX;
        toggleRows = Mathf.Max(1, Mathf.RoundToInt((startY - row.FinalY) / Step) + (partial ? 1 : 0));
        toggleCount = (toggleRows - (partial ? 1 : 0)) * 5 + Mathf.RoundToInt(Mathf.Abs(startX - row.FinalX) / Step);
        return card.y;
    }

    public static void DrawToggles(WidgetRow row, bool world)
    {
        bool bar = Find.PlaySettings.showColonistBar;
        InTray = true;
        try
        {
            Find.PlaySettings.DoPlaySettingsGlobalControls(row, world);
        }
        finally
        {
            InTray = false;
        }
        if (bar != Find.PlaySettings.showColonistBar)
        {
            Find.ColonistBar.MarkColonistsDirty();
        }
    }

    // speed, date, weather and temperature as the game lays them out, on one card
    private static float CornerClock(Map map, float cardRight, float right, float y, bool world)
    {
        bool weather = !world && !map.IsPocketMap;
        bool date = !world || map != null || Find.WorldSelector.AnyObjectOrTileSelected;
        float height = (SpeedAbove ? -4f : TimeControls.TimeButSize.y) + (date ? 4f + DateReadout.Height : 0f) + (weather ? 26f : 0f) + (world ? 0f : 26f);
        Rect card = new Rect(cardRight - 210f, y - height - Pad * 2f, 210f, height + Pad * 2f);
        Card(card);
        float x = right - 193f;
        y -= Pad;
        if (!SpeedAbove)
        {
            GlobalControlsUtility.DoTimespeedControls(x, 200f, ref y);
        }
        if (date)
        {
            y -= 4f;
            float below = y;
            GlobalControlsUtility.DoDate(x, 200f, ref y);
            Rect dateRect = new Rect(right - 120f, y, 120f, below - y);
            if (!world && S.calendar && Widgets.ButtonInvisible(dateRect))
            {
                Window_Calendar.Toggle(dateRect);
            }
        }
        if (world)
        {
            return card.y - 6f;
        }
        if (weather)
        {
            Rect r = new Rect(x - 22f, y - 26f, 230f, 26f);
            map.weatherManager.DoWeatherGUI(r);
            y -= r.height;
        }
        Rect temp = new Rect(x - 100f, y - 26f, 293f, 26f);
        Text.Anchor = TextAnchor.UpperRight;
        Widgets.Label(temp, temperatureString());
        Text.Anchor = TextAnchor.UpperLeft;
        return card.y - 6f;
    }

    public static bool SpeedAbove => Cards && S.hudSpeedAbove;

    // the speed buttons from x, centered on y, measuring how wide they came out for next time
    private static void Speed(float x, float centerY, float speedW)
    {
        speedX = x;
        speedRight = 0f;
        TimeControls.DoTimeControlsGUI(new Rect(x, centerY - TimeControls.TimeButSize.y / 2f, speedW, TimeControls.TimeButSize.y));
        if (speedRight > 0f)
        {
            speedWidth = speedRight;
        }
        speedX = -1f;
    }

    // the speed buttons on their own card in the middle, right above the dock or taskbar
    private static void SpeedAboveBar(bool world)
    {
        float speedW = speedWidth > 0f ? speedWidth : TimeControls.TimeButSize.x * 4f;
        bool dock = S.style == BarStyle.Dock && Dock.Rest.width > 0f;
        float bottom = dock ? Dock.Rest.y : UI.screenHeight - Taskbar.Height;
        float center = dock ? Dock.Rest.center.x : UI.screenWidth / 2f;
        // above whatever sits in the middle over the dock: the drafted pawn's row of gizmos, the selection card or the
        // world map's card. The world map's own buttons (nothing selected) keep it at the bottom, at their left
        float half = speedW / 2f + Pad;
        Rect? beside = null;
        bool Middle(Rect r) => r.width > 0f && r.xMax > center - half && r.x < center + half;
        if (!world && Pane.Shown && Pane.DraftedRow)
        {
            bottom = Mathf.Min(bottom, UI.screenHeight - Taskbar.Reserved - GizmoGrid.DraftedHeight);
        }
        else if (!world && Pane.Shown && Middle(Pane.CardRect))
        {
            bottom = Mathf.Min(bottom, Pane.CardRect.y);
        }
        else if (world && WorldCard.Shown && Middle(WorldCard.Rect))
        {
            bottom = Mathf.Min(bottom, WorldCard.Rect.y);
        }
        else if (world && GizmoGrid.LooseRect is { } loose && Middle(loose))
        {
            beside = loose;
        }
        Rect card = new Rect(center - speedW / 2f - Pad, bottom - 6f - TimeControls.TimeButSize.y - Pad * 2f, speedW + Pad * 2f, TimeControls.TimeButSize.y + Pad * 2f);
        if (beside is { } next)
        {
            card.x = Mathf.Max(0f, next.x - 6f - card.width);
            card.y = next.yMax - card.height;
        }
        Card(card);
        Speed(card.x + Pad, card.center.y, speedW);
    }

    // the tray was drawn next to the dock this frame (off when it didn't fit and the corner was used)
    public static bool TrayShown;
    public static Rect TogglesButton;
    public static int ToggleCount => toggleCount;

    private static Texture2D _toggles;
    private static Texture2D TogglesIcon => _toggles ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Hud/Toggles");

    // right of the dock: toggles in as many rows as fit, the speed buttons, then the hour and date. Where that's too
    // wide for the room right of the dock the toggles fold into one button that opens them above it; where even that
    // doesn't fit, null, and the corner is used
    private static Rect? Tray(WidgetRow row, bool world)
    {
        Rect dock = Dock.Rest;
        float h = dock.height;
        int rows = Mathf.Max(1, Mathf.FloorToInt((h - 8f + WidgetRow.DefaultGap) / Step));
        int cols = Mathf.Max(1, Mathf.CeilToInt(toggleCount / (float)rows));
        int used = Mathf.Max(1, Mathf.CeilToInt(toggleCount / (float)cols));
        float togglesW = cols * Step - WidgetRow.DefaultGap;
        // as wide as the buttons were last time: speed mods change their size and add one
        float speedW = speedWidth > 0f ? speedWidth : TimeControls.TimeButSize.x * 4f;

        bool hasClock = Find.CurrentMap != null;
        Vector2 tile = hasClock ? LongLat() : default;
        int hour = hasClock ? GenDate.HourInteger(Find.TickManager.TicksAbs, tile.x) : 0;
        string hourText = Prefs.TwelveHourClockMode ? hours12[hour] : hours24[hour];
        string season = hasClock ? GenDate.Season(Find.TickManager.TicksAbs, tile).LabelCap() : "";
        string date = hasClock ? DateText(tile) : "";
        Text.Font = GameFont.Small;
        float line = Text.LineHeight;
        bool two = h - 6f >= line * 2f;
        string first = two ? hourText + "  ·  " + season : hourText + "  ·  " + date;
        float clockW = hasClock ? Mathf.Max(Text.CalcSize(first).x, two ? Text.CalcSize(date).x : 0f) : 0f;

        float rest = (SpeedAbove ? 0f : Gap * 2f + 1f + speedW) + (hasClock ? Gap * 2f + 1f + clockW + 4f : 0f) + Pad;
        float room = UI.screenWidth - 6f - (dock.xMax + Gap);
        bool folded = Pad + togglesW + rest > room;
        if (folded)
        {
            togglesW = WidgetRow.IconSize;
            if (Pad + togglesW + rest > room)
            {
                Find.WindowStack.TryRemove(typeof(Window_Toggles), false);
                return null;
            }
        }
        else
        {
            Find.WindowStack.TryRemove(typeof(Window_Toggles), false);
        }
        float width = Pad + togglesW + rest;
        Rect card = new Rect(dock.xMax + Gap, dock.y, width, h);
        Card(card);

        float x = card.x + Pad;
        if (folded)
        {
            Rect button = new Rect(x, card.center.y - WidgetRow.IconSize / 2f, WidgetRow.IconSize, WidgetRow.IconSize);
            TogglesButton = button;
            bool open = Find.WindowStack.IsOpen<Window_Toggles>();
            bool over = Mouse.IsOver(button.ExpandedBy(3f));
            if (open || over)
            {
                GUI.color = open ? Style.Faded(0.35f) : Style.Hover;
                Widgets.DrawAtlas(button.ExpandedBy(3f), Style.Round);
            }
            GUI.color = open || over ? Color.white : Style.Text;
            GUI.DrawTexture(button.ContractedBy(2f), TogglesIcon);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(button, "The toggles: zones, roofs, beauty, the colonist bar...");
            if (Widgets.ButtonInvisible(button.ExpandedBy(3f)))
            {
                if (open)
                {
                    Find.WindowStack.TryRemove(typeof(Window_Toggles));
                }
                else
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    Find.WindowStack.Add(new Window_Toggles(button, world));
                }
            }
        }
        else
        {
            float startY = card.y + (h - (used * Step - WidgetRow.DefaultGap)) / 2f;
            row.Init(x, startY, UIDirection.RightThenDown, togglesW);
            DrawToggles(row, world);
            CountToggles(row, x, startY, cols);
        }
        x += togglesW + Gap;
        Divider(x, card);
        x += 1f + Gap;

        if (!SpeedAbove)
        {
            Speed(x, card.center.y, speedW);
            if (!hasClock)
            {
                return card;
            }
            x += speedW + Gap;
            Divider(x, card);
            x += 1f + Gap;
        }
        else if (!hasClock)
        {
            return card;
        }

        Rect clock = new Rect(x, card.y, card.xMax - x - Pad, h);
        float top = card.center.y - (two ? line : line / 2f);
        Text.Anchor = TextAnchor.MiddleLeft;
        GUI.color = Style.Text;
        Widgets.Label(new Rect(clock.x, top, clock.width + 4f, line), hourText);
        GUI.color = Style.Dim;
        Widgets.Label(new Rect(clock.x + Text.CalcSize(hourText).x, top, clock.width, line), "  ·  " + (two ? season : date));
        if (two)
        {
            GUI.color = Style.Text;
            Widgets.Label(new Rect(clock.x, top + line, clock.width + 4f, line), date);
        }
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        if (Mouse.IsOver(clock))
        {
            Widgets.DrawHighlight(clock.ContractedBy(0f, 4f));
            TooltipHandler.TipRegion(clock, new TipSignal(DateTip(tile) + (S.calendar ? "\n\n" + "Click for the calendar".Colorize(Style.Dim) : ""), 86423));
        }
        ClockRect = clock;
        if (S.calendar && Widgets.ButtonInvisible(clock))
        {
            Window_Calendar.Toggle(clock);
        }
        return card;
    }

    // where the weather card was last drawn, the lines under the mouse go next to it
    public static Rect WeatherCard;

    // left of the dock: the weather and the temperature where the mouse is
    private static void Weather(Map map)
    {
        Rect dock = Dock.Rest;
        float h = dock.height;
        Text.Font = GameFont.Small;
        float line = Text.LineHeight;
        bool two = h - 6f >= line * 2f;
        string temp = temperatureString();
        WeatherDef weather = map.IsPocketMap ? null : map.weatherManager.CurWeatherPerceived;
        string sky = weather?.LabelCap ?? "";
        float w = two ? Mathf.Max(Text.CalcSize(sky).x, Text.CalcSize(temp).x) : Text.CalcSize(sky.NullOrEmpty() ? temp : sky + "  ·  " + temp).x;
        Rect card = new Rect(dock.x - Gap - w - Pad * 2f - 4f, dock.y, w + Pad * 2f + 4f, h);
        WeatherCard = card;
        Card(card);
        Rect inner = card.ContractedBy(Pad, 0f);
        Text.Anchor = TextAnchor.MiddleLeft;
        if (two && weather != null)
        {
            float top = card.center.y - line;
            GUI.color = Style.Text;
            Widgets.Label(new Rect(inner.x, top, inner.width + 4f, line), sky);
            GUI.color = Style.Dim;
            Widgets.Label(new Rect(inner.x, top + line, inner.width + 4f, line), temp);
        }
        else
        {
            GUI.color = Style.Text;
            Widgets.Label(new Rect(inner.x, card.center.y - line / 2f, inner.width + 4f, line), sky.NullOrEmpty() ? temp : sky + "  ·  " + temp);
        }
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        if (weather != null && !weather.description.NullOrEmpty())
        {
            TooltipHandler.TipRegion(card, weather.description);
        }
    }

    public static void CountToggles(WidgetRow row, float x, float startY, int cols)
    {
        int count = Mathf.RoundToInt((row.FinalY - startY) / Step) * cols + Mathf.RoundToInt((row.FinalX - x) / Step);
        if (count > 0)
        {
            toggleCount = count;
        }
    }

    private static void Divider(float x, Rect card)
    {
        Widgets.DrawBoxSolid(new Rect(x, card.y + card.height * 0.22f, 1f, card.height * 0.56f), Style.Divider);
    }

    private static Vector2 LongLat()
    {
        return Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile);
    }

    private static int dateDay = -1;
    private static string dateText;

    private static string DateText(Vector2 tile)
    {
        int day = GenDate.DaysPassed;
        if (day != dateDay || dateText == null)
        {
            dateText = GenDate.DateReadoutStringAt(Find.TickManager.TicksAbs, tile);
            dateDay = day;
        }
        return dateText;
    }

    // the same as the game's tooltip on the date
    private static string DateTip(Vector2 tile)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 4; i++)
        {
            Quadrum q = (Quadrum)i;
            sb.AppendLine(q.Label() + " - " + q.GetSeason(tile.y).LabelCap());
        }
        Season season = GenDate.Season(Find.TickManager.TicksAbs, tile);
        return "DateReadoutTip".Translate(GenDate.DaysPassed, 15, season.LabelCap(), 15, GenDate.Quadrum(GenTicks.TicksAbs, tile.x).Label(), sb.ToString());
    }

    // a toggle's little check or cross: on gets an accent line under the icon, off dims the icon
    public static void ToggleMark(Rect rect, Texture tex)
    {
        if (!InTray)
        {
            GUI.DrawTexture(rect, tex);
            return;
        }
        Rect icon = new Rect(rect.x - rect.width, rect.y, rect.width * 2f, rect.height * 2f);
        Color old = GUI.color;
        if (tex == Widgets.CheckboxOnTex)
        {
            GUI.color = Style.Accent;
            GUI.DrawTexture(new Rect(icon.x + 5f, icon.yMax + 1f, icon.width - 10f, 2f), BaseContent.WhiteTex);
        }
        else
        {
            Color dim = Back;
            dim.a = 0.62f;
            GUI.color = dim;
            Widgets.DrawAtlas(icon.ExpandedBy(1f), Style.Round);
        }
        GUI.color = old;
    }

    // the hour and date in the tray, where the calendar opens from
    public static Rect ClockRect;

    // where the tray put the speed buttons (-1 elsewhere), and how far right they went
    public static float speedX = -1f;
    private static float speedRight, speedWidth;

    public static bool SpeedButton(Rect rect, Texture2D tex, bool doMouseoverSound, string tooltip)
    {
        if (speedX >= 0f)
        {
            speedRight = Mathf.Max(speedRight, rect.xMax);
        }
        int speed = S.modernSpeed ? SpeedOf(tex) : -1;
        lastModern = speed >= 0;
        if (!lastModern)
        {
            return Widgets.ButtonImage(rect, tex, doMouseoverSound, tooltip);
        }
        // our icons; while a window pauses the game (or a fight keeps it at normal speed) the speeds that can't be
        // picked are dimmed, where the game crosses them out with a line
        TickManager tm = Find.TickManager;
        bool off = tm.ForcePaused ? speed != (int)TimeSpeed.Paused : tm.slower.ForcedNormalSpeed && speed > (int)TimeSpeed.Normal;
        bool selected = (int)(tm.ForcePaused ? TimeSpeed.Paused : tm.CurTimeSpeed) == speed;
        bool over = !off && Mouse.IsOver(rect);
        if (selected || over)
        {
            GUI.color = selected ? Style.Faded(over ? 0.5f : 0.35f) : Style.Hover;
            Widgets.DrawAtlas(rect.ContractedBy(1f), Style.Round);
        }
        GUI.color = off ? Style.Disabled : selected || over ? Color.white : Style.Text;
        float size = Mathf.Min(rect.height, rect.width) - 4f;
        GUI.DrawTexture(new Rect(rect.center.x - size / 2f, rect.center.y - size / 2f, size, size), SpeedIcons[speed]);
        GUI.color = Color.white;
        if (doMouseoverSound)
        {
            MouseoverSounds.DoRegion(rect);
        }
        TooltipHandler.TipRegion(rect, tooltip);
        return Widgets.ButtonInvisible(rect, false);
    }

    // Smart Speed draws the buttons from its own copy of the pictures, where the fastest one is its own
    private static Texture2D[] smartSpeed;
    private static bool smartSpeedLooked;

    private static int SpeedOf(Texture tex)
    {
        int i = System.Array.IndexOf(TexButton.SpeedButtonTextures, tex);
        if (i >= 0)
        {
            return i;
        }
        if (!smartSpeedLooked)
        {
            smartSpeedLooked = true;
            smartSpeed = AccessTools.TypeByName("SmartSpeed.AlternateButtons") is { } t ? AccessTools.Field(t, "SpeedButtonTextures")?.GetValue(null) as Texture2D[] : null;
        }
        return smartSpeed != null ? System.Array.IndexOf(smartSpeed, tex) : -1;
    }

    private static bool lastModern;
    private static Texture2D[] speedIcons;
    private static Texture2D[] SpeedIcons => speedIcons ??= new[] { "Pause", "Normal", "Fast", "Superfast", "Ultrafast" }.Select(n => ContentFinder<Texture2D>.Get("UI/JustDesktop/Hud/Speed" + n)).ToArray();

    // the game's line across the speeds that can't be picked; ours dims them instead
    public static void SpeedLine(float x, float y, float length)
    {
        if (!S.modernSpeed)
        {
            Widgets.DrawLineHorizontal(x, y, length);
        }
    }

    // the selected speed: a soft rounded fill in the accent instead of the game's square highlight
    public static void SpeedHighlight(Rect rect, Texture tex)
    {
        if (lastModern)
        {
            return;
        }
        if (!Cards)
        {
            GUI.DrawTexture(rect, tex);
            return;
        }
        GUI.color = Style.Faded(0.35f);
        Widgets.DrawAtlas(rect.ContractedBy(1f), Style.Round);
        GUI.color = Color.white;
    }

    // the dark shadow the game puts behind text over the map; the cards do that now
    public static void WinterShadow(Rect rect)
    {
        if (!Cards)
        {
            GenUI.DrawTextWinterShadow(rect);
        }
    }

    public static void NoShadow(Rect rect)
    {
        if (!Cards)
        {
            Widgets.DrawShadowAround(rect);
        }
    }

    // an alert's red or yellow background and its hover, rounded inside the alerts card
    public static void AlertBack(Rect rect, Texture tex)
    {
        if (!Cards)
        {
            GUI.DrawTexture(rect, tex);
            return;
        }
        Rect r = new Rect(rect.x - 2f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
        if (tex == TexUI.HighlightTex)
        {
            GUI.color = Style.Hover;
        }
        if (GUI.color.a > 0f)
        {
            Widgets.DrawAtlas(r, Style.Round);
        }
    }

    // the letter's name under its icon on a small rounded pill
    public static void LetterTexture(Rect rect, Texture tex)
    {
        if (!Cards || tex != TexUI.GrayTextBG)
        {
            GUI.DrawTexture(rect, tex);
            return;
        }
        Color old = GUI.color;
        GUI.color = Back;
        Widgets.DrawAtlas(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 3f), Style.Round);
        GUI.color = old;
    }

    // a message's background, rounded in the card color, fading with the message
    public static void MessageBack(Rect rect, Texture tex)
    {
        if (!Cards)
        {
            GUI.DrawTexture(rect, tex);
            return;
        }
        Color c = Back;
        c.a *= Mathf.Clamp01(GUI.color.a / 0.8f);
        GUI.color = c;
        Widgets.DrawAtlas(rect, Style.Round);
    }

    // where the lines under the mouse went last frame, for the card behind them this frame
    private static Rect readout, lastReadout;

    public static void ReadoutStart()
    {
        lastReadout = readout;
        readout = default;
    }

    public static Rect LastReadout => lastReadout;

    private static void Note(Rect rect, string text)
    {
        if (!Cards)
        {
            return;
        }
        Vector2 size = Text.CalcSize(text);
        Rect r = new Rect(rect.x, rect.y, size.x, size.y);
        readout = readout.width <= 0f ? r : Rect.MinMaxRect(Mathf.Min(readout.xMin, r.xMin), Mathf.Min(readout.yMin, r.yMin), Mathf.Max(readout.xMax, r.xMax), Mathf.Max(readout.yMax, r.yMax));
    }

    public static void ReadoutLabel(Rect rect, string text)
    {
        Note(rect, text);
        Widgets.Label(rect, text);
    }

    public static void ReadoutLabel(Rect rect, TaggedString text)
    {
        Note(rect, text.RawText);
        Widgets.Label(rect, text);
    }
}

[HarmonyPatch(typeof(GlobalControls), nameof(GlobalControls.GlobalControlsOnGUI))]
public static class Hud_GlobalControls
{
    public static bool Prefix(GlobalControls __instance)
    {
        if (!Hud.Cards)
        {
            return true;
        }
        if (Event.current.type != EventType.Layout)
        {
            Hud.Controls(rowVisibility(__instance), false);
        }
        return false;
    }

    private static readonly AccessTools.FieldRef<GlobalControls, WidgetRow> rowVisibility = AccessTools.FieldRefAccess<GlobalControls, WidgetRow>("rowVisibility");
}

[HarmonyPatch(typeof(WorldGlobalControls), nameof(WorldGlobalControls.WorldGlobalControlsOnGUI))]
public static class Hud_WorldControls
{
    private static readonly AccessTools.FieldRef<WorldGlobalControls, WidgetRow> rowVisibility = AccessTools.FieldRefAccess<WorldGlobalControls, WidgetRow>("rowVisibility");

    public static bool Prefix(WorldGlobalControls __instance)
    {
        if (!Hud.Cards || Current.ProgramState != ProgramState.Playing)
        {
            return true;
        }
        if (Event.current.type != EventType.Layout)
        {
            Hud.Controls(rowVisibility(__instance), true);
        }
        return false;
    }
}

[HarmonyPatch(typeof(WidgetRow), nameof(WidgetRow.ToggleableIcon))]
public static class Hud_Toggle
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return Gizmos.Swap(instructions, Gizmos.DrawTexture, AccessTools.Method(typeof(Hud), nameof(Hud.ToggleMark)));
    }
}

[HarmonyPatch(typeof(TimeControls), nameof(TimeControls.DoTimeControlsGUI))]
public static class Hud_Speed
{
    // in the tray the buttons start where the tray says, speed mods move them left for the game's corner
    [HarmonyPriority(Priority.Last)]
    public static void Prefix(ref Rect timerRect)
    {
        if (Hud.speedX >= 0f)
        {
            timerRect.x = Hud.speedX;
        }
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo button = AccessTools.Method(typeof(Widgets), nameof(Widgets.ButtonImage), new[] { typeof(Rect), typeof(Texture2D), typeof(bool), typeof(string) });
        instructions = Gizmos.Swap(instructions, button, AccessTools.Method(typeof(Hud), nameof(Hud.SpeedButton)));
        instructions = Gizmos.Swap(instructions, AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawLineHorizontal), new[] { typeof(float), typeof(float), typeof(float) }), AccessTools.Method(typeof(Hud), nameof(Hud.SpeedLine)));
        return Gizmos.Swap(instructions, Gizmos.DrawTexture, AccessTools.Method(typeof(Hud), nameof(Hud.SpeedHighlight)));
    }
}

// one card behind all the alerts, each alert's color rounded inside it
[HarmonyPatch(typeof(AlertsReadout), nameof(AlertsReadout.AlertsReadoutOnGUI))]
public static class Hud_Alerts
{
    public static void Prefix(List<Alert> ___activeAlerts)
    {
        if (!Hud.Cards || Event.current.type != EventType.Repaint || ___activeAlerts.Count == 0)
        {
            return;
        }
        float h = Find.Alerts.AlertsHeight;
        float top = Mathf.Max(0f, Find.LetterStack.LastTopY - h);
        Hud.Card(new Rect(UI.screenWidth - 162f, top - 5f, 158f, h + 10f));
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo shadow = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawShadowAround));
        return Gizmos.Swap(instructions, shadow, AccessTools.Method(typeof(Hud), nameof(Hud.NoShadow)));
    }
}

[HarmonyPatch(typeof(Alert), nameof(Alert.DrawAt))]
public static class Hud_Alert
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return Gizmos.Swap(instructions, Gizmos.DrawTexture, AccessTools.Method(typeof(Hud), nameof(Hud.AlertBack)));
    }
}

[HarmonyPatch(typeof(Letter), nameof(Letter.DrawButtonAt))]
public static class Hud_Letter
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo shadow = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawShadowAround));
        instructions = Gizmos.Swap(instructions, shadow, AccessTools.Method(typeof(Hud), nameof(Hud.NoShadow)));
        return Gizmos.Swap(instructions, Gizmos.DrawTexture, AccessTools.Method(typeof(Hud), nameof(Hud.LetterTexture)));
    }
}

// the message's drawing is a lambda handed to an immediate window
[HarmonyPatch]
public static class Hud_Message
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        var types = new List<System.Type> { typeof(Message) };
        types.AddRange(typeof(Message).GetNestedTypes(AccessTools.all));
        foreach (System.Type t in types)
        {
            foreach (MethodInfo m in t.GetMethods(AccessTools.allDeclared))
            {
                if (m.Name.StartsWith("<Draw>"))
                {
                    yield return m;
                }
            }
        }
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return Gizmos.Swap(instructions, Gizmos.DrawTexture, AccessTools.Method(typeof(Hud), nameof(Hud.MessageBack)));
    }
}

// resources on a card, moved a little to the right to sit inside it
[HarmonyPatch(typeof(ResourceReadout), nameof(ResourceReadout.ResourceReadoutOnGUI))]
public static class Hud_Resources
{
    private static bool grouped;

    public static void Prefix(float ___lastDrawnHeight)
    {
        // the picking buttons stay with the cards off too, only the card itself goes
        if (!Resources.On || Event.current.type == EventType.Layout || Current.ProgramState != ProgramState.Playing || Find.MainTabsRoot.OpenTab == MainButtonDefOf.Menu)
        {
            return;
        }
        float width = Prefs.ResourceReadoutCategorized ? 124f : 110f;
        float x = Prefs.ResourceReadoutCategorized ? 2f : 7f;
        float h = Mathf.Min(___lastDrawnHeight, UI.screenHeight - 7f - 200f);
        // the card always shows, with the buttons to pick what's on it at the top
        Rect card = new Rect(x, 3f, width + 10f, Mathf.Max(h, 0f) + 8f + Resources.Header);
        if (Hud.Cards && Event.current.type == EventType.Repaint)
        {
            Hud.Card(card);
        }
        Resources.DrawHeader(card);
        Widgets.BeginGroup(new Rect(4f, Resources.Header, UI.screenWidth, UI.screenHeight));
        grouped = true;
    }

    public static void Finalizer()
    {
        if (grouped)
        {
            grouped = false;
            Widgets.EndGroup();
        }
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo shadow = AccessTools.Method(typeof(GenUI), nameof(GenUI.DrawTextWinterShadow));
        return Gizmos.Swap(instructions, shadow, AccessTools.Method(typeof(Hud), nameof(Hud.WinterShadow)));
    }
}

// the lines under the mouse at the bottom left on a card the size they took last frame
[HarmonyPatch]
public static class Hud_Mouseover
{
    private static readonly System.Func<MouseoverReadout, bool> shouldShow = AccessTools.MethodDelegate<System.Func<MouseoverReadout, bool>>(AccessTools.PropertyGetter(typeof(MouseoverReadout), "ShouldShow"));

    public static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI));
        yield return AccessTools.Method(typeof(MouseoverReadout), "DrawGas");
    }

    public static void Prefix(MouseoverReadout __instance, MethodBase __originalMethod)
    {
        if (__originalMethod.Name != nameof(MouseoverReadout.MouseoverReadoutOnGUI) || Event.current.type != EventType.Repaint)
        {
            return;
        }
        Hud.ReadoutStart();
        if (!Loading.Ready || !shouldShow(__instance))
        {
            return;
        }
        Rect last = Hud.LastReadout;
        bool cards = Hud.Cards && last.width > 0f;
        if (cards && Hud.TrayShown && Hud.WeatherCard.width > 0f)
        {
            // next to the weather card, at least as tall as the dock and lined up with its bottom. The lines keep to the bottom
            // so they don't jump when there are more or fewer: two lines sit in the middle, more go up from there.
            // The game draws them where it always does, a group moves them
            Rect dock = Dock.Rest;
            float bottom = Mathf.Max(4f, Mathf.Round((dock.height - 41f) / 2f));
            float w = last.width + 16f, h = Mathf.Max(last.height + bottom + 4f, dock.height);
            Rect card = new Rect(Hud.WeatherCard.x - 10f - w, dock.yMax - h, w, h);
            // where that would start off the screen, the game's corner below
            if (card.x >= 6f)
            {
                Hud.Card(card);
                Vector2 offset = new Vector2(Mathf.Round(card.x + 8f - last.x), Mathf.Round(card.yMax - bottom - last.yMax));
                Widgets.BeginGroup(new Rect(offset.x, offset.y, UI.screenWidth, UI.screenHeight));
                moved = true;
                return;
            }
        }
        // the game's corner, raised above the taskbar when it's taller than the game's bar
        float lift = Mathf.Max(0f, Taskbar.Reserved - 35f);
        if (lift > 0f)
        {
            Widgets.BeginGroup(new Rect(0f, -lift, UI.screenWidth, UI.screenHeight + lift));
            moved = true;
        }
        if (cards)
        {
            Hud.Card(last.ExpandedBy(8f, 4f));
        }
    }

    private static bool moved;

    public static void Finalizer(MethodBase __originalMethod)
    {
        // DrawGas runs inside the main one
        if (moved && __originalMethod.Name == nameof(MouseoverReadout.MouseoverReadoutOnGUI))
        {
            moved = false;
            Widgets.EndGroup();
        }
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo shadow = AccessTools.Method(typeof(GenUI), nameof(GenUI.DrawTextWinterShadow));
        MethodInfo labelString = AccessTools.Method(typeof(Widgets), nameof(Widgets.Label), new[] { typeof(Rect), typeof(string) });
        MethodInfo labelTagged = AccessTools.Method(typeof(Widgets), nameof(Widgets.Label), new[] { typeof(Rect), typeof(TaggedString) });
        instructions = Gizmos.Swap(instructions, shadow, AccessTools.Method(typeof(Hud), nameof(Hud.WinterShadow)));
        instructions = Gizmos.Swap(instructions, labelString, AccessTools.Method(typeof(Hud), nameof(Hud.ReadoutLabel), new[] { typeof(Rect), typeof(string) }));
        return Gizmos.Swap(instructions, labelTagged, AccessTools.Method(typeof(Hud), nameof(Hud.ReadoutLabel), new[] { typeof(Rect), typeof(TaggedString) }));
    }
}

// the toggles folded out of the tray, in a small card above their button
public class Window_Toggles : Window
{
    private const float Pad = 8f;
    private const int Columns = 6;
    private const float Step = WidgetRow.IconSize + WidgetRow.DefaultGap;

    private readonly Rect anchor;
    private readonly bool world;

    protected override float Margin => 0f;

    private static int Rows => Mathf.Max(1, Mathf.CeilToInt(Hud.ToggleCount / (float)Columns));
    private static int Cols => Mathf.Clamp(Hud.ToggleCount, 1, Columns);

    public override Vector2 InitialSize => new(Pad * 2f + Cols * Step - WidgetRow.DefaultGap, Pad * 2f + Rows * Step - WidgetRow.DefaultGap);

    public Window_Toggles(Rect anchor, bool world)
    {
        this.anchor = anchor;
        this.world = world;
        doWindowBackground = false;
        drawShadow = false;
        closeOnClickedOutside = true;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
        layer = WindowLayer.GameUI;
        soundAppear = null;
    }

    protected override void SetInitialSizeAndPosition()
    {
        Vector2 size = InitialSize;
        float x = Mathf.Clamp(anchor.center.x - size.x / 2f, 6f, UI.screenWidth - size.x - 6f);
        windowRect = new Rect(x, anchor.y - 14f - size.y, size.x, size.y);
    }

    public override void DoWindowContents(Rect inRect)
    {
        Pane.CardBack(inRect);
        WidgetRow row = new WidgetRow();
        row.Init(Pad, Pad, UIDirection.RightThenDown, Cols * Step - WidgetRow.DefaultGap);
        Hud.DrawToggles(row, world);
        Hud.CountToggles(row, Pad, Pad, Cols);
        // the count can change (world view, mods), the card follows it
        Vector2 size = InitialSize;
        if (size != windowRect.size)
        {
            windowRect = new Rect(windowRect.xMax - size.x, windowRect.yMax - size.y, size.x, size.y);
        }
    }
}
