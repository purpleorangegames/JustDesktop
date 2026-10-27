using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JustDesktop;

// a bell under the letters opens a notification center: the letters waiting now, newest first, to open or dismiss,
// and the ones already gone from the stack (opened or dismissed) from today and yesterday, at least the last ten,
// to open again or put back in the stack
public static class Notifications
{
    private static Texture2D _bell;
    public static Texture2D Bell => _bell ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Hud/Bell");
    private static Texture2D _putBack;
    public static Texture2D PutBack => _putBack ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Hud/PutBack");

    public static bool On => JustDesktopMod.Settings.notifications && Hud.Cards;

    // the bell at the bottom right, under the letters; returns where the letters start
    public static float DrawBell(float y)
    {
        if (!On)
        {
            return y;
        }
        Rect r = new Rect(UI.screenWidth - 6f - 40f, y - 40f, 40f, 40f);
        bool open = Find.WindowStack.IsOpen<Window_Notifications>();
        bool over = Mouse.IsOver(r);
        Hud.Card(r);
        if (open || over)
        {
            GUI.color = open ? Style.Faded(0.35f) : Style.Hover;
            Widgets.DrawAtlas(r.ContractedBy(2f), Style.Round);
        }
        GUI.color = open || over ? Color.white : Style.Text;
        GUI.DrawTexture(r.ContractedBy(8f), Bell);
        int count = Find.LetterStack.LettersListForReading.Count;
        if (count > 0)
        {
            Text.Font = GameFont.Small;
            string n = count > 99 ? "99" : count.ToString();
            float w = Mathf.Max(20f, Text.CalcSize(n).x + 8f);
            Rect badge = new Rect(r.xMax - w + 4f, r.y - 5f, w, 20f);
            GUI.color = Style.Accent;
            Widgets.DrawAtlas(badge, Style.Round);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(badge, n);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
        GUI.color = Color.white;
        TooltipHandler.TipRegion(r, "Notifications: the letters now and the ones already opened or dismissed");
        if (Widgets.ButtonInvisible(r))
        {
            if (open)
            {
                Find.WindowStack.TryRemove(typeof(Window_Notifications));
            }
            else
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                Find.WindowStack.Add(new Window_Notifications(r));
            }
        }
        return r.y - 6f;
    }

    // back in the stack where it was by time, without the arrival sound or pausing
    public static void Return(Letter letter)
    {
        List<Letter> letters = Find.LetterStack.LettersListForReading;
        if (letters.Contains(letter) || !letter.CanShowInLetterStack)
        {
            return;
        }
        int i = letters.FindIndex(l => l.arrivalTick > letter.arrivalTick);
        letters.Insert(i < 0 ? letters.Count : i, letter);
        letter.arrivalTime = Time.time;
        SoundDefOf.Tick_High.PlayOneShotOnCamera();
    }
}

public class Window_Notifications : Window
{
    private const float Width = 380f;
    private const float Row = 46f;
    private const int Recent = 10;

    private readonly Rect anchor;
    private Vector2 scroll;

    private readonly List<Letter> now = new();
    private readonly List<Letter> gone = new();

    // its top stays clear of the top right corner, where the game's and other mods' minimaps and buttons go
    private const float TopClear = 340f;

    public override Vector2 InitialSize => new(Width, Mathf.Clamp(anchor.y - 8f - TopClear, Mathf.Min(260f, anchor.y - 16f), 620f));

    protected override float Margin => 0f;

    public Window_Notifications(Rect anchor)
    {
        this.anchor = anchor;
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
        windowRect = new Rect(UI.screenWidth - 6f - size.x, anchor.y - 8f - size.y, size.x, size.y);
    }

    private void Collect()
    {
        now.Clear();
        now.AddRange(Find.LetterStack.LettersListForReading);
        now.Reverse();
        gone.Clear();
        int yesterday = Window_Calendar.DayKey(GenTicks.TicksAbs) - 1;
        var archived = Find.Archive.ArchivablesListForReading.OfType<Letter>().Where(l => !now.Contains(l)).OrderByDescending(l => ((IArchivable)l).CreatedTicksGame).ToList();
        for (int i = 0; i < archived.Count; i++)
        {
            if (i < Recent || Day(archived[i]) >= yesterday)
            {
                gone.Add(archived[i]);
            }
        }
    }

    private static int Day(Letter l) => Window_Calendar.DayKey(GenDate.TickGameToAbs(((IArchivable)l).CreatedTicksGame));

    public override void DoWindowContents(Rect inRect)
    {
        bool active = ModernWidgets.Active;
        ModernWidgets.Active = ModernWidgets.Enabled;
        try
        {
            Contents(inRect);
        }
        finally
        {
            ModernWidgets.Active = active;
        }
    }

    private void Contents(Rect inRect)
    {
        Collect();
        Pane.CardBack(inRect);
        Rect inner = inRect.ContractedBy(10f);
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(inner.x + 2f, inner.y, inner.width, 32f), "Notifications");
        Text.Font = GameFont.Small;

        float headerY = inner.y + 36f;
        Rect outRect = new Rect(inner.x, headerY, inner.width, inner.yMax - headerY);
        int today = Window_Calendar.DayKey(GenTicks.TicksAbs);
        int groups = gone.Select(l => Group(Day(l), today)).Distinct().Count();
        float h = 28f + Mathf.Max(1, now.Count) * Row + 34f + groups * 28f + Mathf.Max(1, gone.Count) * Row;
        Rect view = new Rect(0f, 0f, outRect.width - (h > outRect.height ? 14f : 0f), h);
        Widgets.BeginScrollView(outRect, ref scroll, view);
        float y = 0f;

        // waiting now, with a button to dismiss the ones that can be
        Heading(new Rect(0f, y, view.width, 26f), "Now");
        if (now.Any(l => l.CanDismissWithRightClick))
        {
            Rect all = new Rect(view.width - 90f, y + 1f, 90f, 24f);
            if (Widgets.ButtonText(all, "Dismiss all"))
            {
                foreach (Letter l in now.Where(l => l.CanDismissWithRightClick).ToList())
                {
                    Find.LetterStack.RemoveLetter(l);
                }
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }
        y += 28f;
        if (now.Count == 0)
        {
            Empty(new Rect(0f, y, view.width, Row), "Nothing waiting");
            y += Row;
        }
        foreach (Letter l in now)
        {
            DrawRow(new Rect(0f, y, view.width, Row - 4f), l, true, today);
            y += Row;
        }

        y += 6f;
        Heading(new Rect(0f, y, view.width, 26f), "Already seen");
        y += 28f;
        if (gone.Count == 0)
        {
            Empty(new Rect(0f, y, view.width, Row), "Nothing yet");
        }
        string last = null;
        foreach (Letter l in gone)
        {
            string group = Group(Day(l), today);
            if (group != last)
            {
                GUI.color = Style.Dim;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(4f, y + 6f, view.width, 20f), group);
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                y += 28f;
                last = group;
            }
            DrawRow(new Rect(0f, y, view.width, Row - 4f), l, false, today);
            y += Row;
        }
        Widgets.EndScrollView();
    }

    private static string Group(int day, int today) => day >= today ? "Today" : day == today - 1 ? "Yesterday" : "Earlier";

    private static void Heading(Rect r, string label)
    {
        GUI.color = Style.Text;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(r.x + 4f, r.y, r.width, r.height), label);
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
    }

    private static void Empty(Rect r, string label)
    {
        GUI.color = Style.Dim;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(r, label);
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
    }

    private static string When(Letter l, int today)
    {
        long abs = GenDate.TickGameToAbs(((IArchivable)l).CreatedTicksGame);
        Map map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
        Vector2 at = map != null ? Find.WorldGrid.LongLatOf(map.Tile) : Vector2.zero;
        int day = Day(l);
        string hour = GenDate.HourInteger(abs, at.x) + "LetterHour".Translate();
        return day >= today ? "Today, " + hour : day == today - 1 ? "Yesterday, " + hour : GenDate.DateFullStringWithHourAt(abs, at);
    }

    private void DrawRow(Rect r, Letter l, bool waiting, int today)
    {
        bool over = Mouse.IsOver(r);
        GUI.color = over ? new Color(1f, 1f, 1f, 0.1f) : new Color(1f, 1f, 1f, 0.04f);
        Widgets.DrawAtlas(r, ModernWidgets.Fill);
        // the letter's color as a bar at the left, its picture next to it
        GUI.color = l.def.color;
        Widgets.DrawAtlas(new Rect(r.x + 4f, r.y + 6f, 4f, r.height - 12f), ModernWidgets.Fill);
        GUI.color = waiting ? l.def.color : new Color(l.def.color.r, l.def.color.g, l.def.color.b, 0.6f);
        GUI.DrawTexture(new Rect(r.x + 14f, r.center.y - 11f, 26f, 22f), l.def.Icon);
        GUI.color = Color.white;

        Rect button = new Rect(r.xMax - 30f, r.center.y - 12f, 24f, 24f);
        bool canButton = waiting ? l.CanDismissWithRightClick : l.CanShowInLetterStack;
        float textRight = canButton ? button.x - 4f : r.xMax - 6f;
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = waiting ? Color.white : Style.Text;
        Text.WordWrap = false;
        Widgets.Label(new Rect(r.x + 48f, r.y + 4f, textRight - r.x - 48f, 22f), ((IArchivable)l).ArchivedLabel.Truncate(textRight - r.x - 48f));
        Text.WordWrap = true;
        Text.Font = GameFont.Tiny;
        GUI.color = Style.Dim;
        Widgets.Label(new Rect(r.x + 48f, r.y + 23f, textRight - r.x - 48f, 18f), When(l, today));
        Text.Font = GameFont.Small;
        GUI.color = Color.white;

        if (canButton)
        {
            bool overButton = Mouse.IsOver(button);
            if (overButton)
            {
                GUI.color = waiting ? Style.CloseHover : Style.Faded(0.5f);
                GUI.DrawTexture(button, Style.Circle);
            }
            GUI.color = overButton ? Color.white : Style.Dim;
            GUI.DrawTexture(button.ContractedBy(waiting ? 6f : 4f), waiting ? Style.Close : Notifications.PutBack);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(button, waiting ? "Dismiss" : "Put it back with the letters");
            if (Widgets.ButtonInvisible(button))
            {
                if (waiting)
                {
                    Find.LetterStack.RemoveLetter(l);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                else
                {
                    Notifications.Return(l);
                }
                return;
            }
        }
        if (over)
        {
            TooltipHandler.TipRegion(r, new TipSignal(() => ((IArchivable)l).ArchivedTooltip, l.ID ^ 0x51f3));
        }
        if (Widgets.ButtonInvisible(r))
        {
            if (waiting)
            {
                l.OpenLetter();
            }
            else
            {
                ((IArchivable)l).OpenArchived();
            }
        }
    }
}
