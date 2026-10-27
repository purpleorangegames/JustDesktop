using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace JustDesktop;

// the calendar's events kept in the save (option, off by default): the game's archive lets go of the oldest letters
// after about 200 entries, so a copy of each letter (day, title, kind and text) is kept here as it arrives, and the
// calendar shows these for the days the archive no longer has
public class CalendarHistory : GameComponent
{
    public class Kept : IExposable
    {
        public int tick;
        public string label;
        public LetterDef def;
        public string text;

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref label, "label");
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref text, "text");
        }
    }

    private List<Kept> kept = new();
    private readonly HashSet<(int, string)> known = new();

    public CalendarHistory(Game game)
    {
    }

    public static CalendarHistory Get => Current.Game?.GetComponent<CalendarHistory>();

    public static bool On => JustDesktopMod.Settings.calendar && JustDesktopMod.Settings.calendarKeep;

    public List<Kept> All => kept;

    public static (int, string) Key(int tick, string label) => (tick, label);

    public void Keep(Letter letter)
    {
        IArchivable a = letter;
        if (!known.Add(Key(a.CreatedTicksGame, a.ArchivedLabel)))
        {
            return;
        }
        kept.Add(new Kept { tick = a.CreatedTicksGame, label = a.ArchivedLabel, def = letter.def, text = a.ArchivedTooltip });
    }

    // the letters still in the archive, once a session: when the game loads with the option on, or the first time it's
    // needed after turning it on
    private bool filled;

    public void KeepArchive()
    {
        if (filled)
        {
            return;
        }
        filled = true;
        foreach (IArchivable a in Find.Archive.ArchivablesListForReading)
        {
            if (a is Letter letter)
            {
                Keep(letter);
            }
        }
    }

    public override void FinalizeInit()
    {
        if (On)
        {
            KeepArchive();
        }
    }

    public override void ExposeData()
    {
        Scribe_Collections.Look(ref kept, "kept", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            kept ??= new List<Kept>();
            kept.RemoveAll(k => k == null || k.def == null);
            known.Clear();
            foreach (Kept k in kept)
            {
                known.Add(Key(k.tick, k.label));
            }
        }
    }
}

// each letter as it goes into the archive
[HarmonyPatch(typeof(Archive), nameof(Archive.Add))]
public static class CalendarHistory_Add
{
    public static void Postfix(IArchivable archivable, bool __result)
    {
        if (__result && archivable is Letter letter && CalendarHistory.On && Current.ProgramState == ProgramState.Playing)
        {
            CalendarHistory history = CalendarHistory.Get;
            history?.KeepArchive();
            history?.Keep(letter);
        }
    }
}
