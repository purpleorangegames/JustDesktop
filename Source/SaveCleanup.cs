using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// before removing the mod: takes what it keeps in the save (restock letters, the research queue, the calendar's kept
// events, who builds what) out of the game that's open, so the next save has nothing of this mod and loads without it with no errors.
// Just emptying them isn't enough, the game still writes each one into the save by its class name
public static class SaveCleanup
{
    private static Game cleared;

    private static bool InGame => Current.ProgramState == ProgramState.Playing && Current.Game != null;

    public static bool Has(Game game) => game.components.Any(c => c is RestockWatch or ResearchQueue or CalendarHistory or BuilderPick);

    public static void Clear()
    {
        Game game = Current.Game;
        game.components.RemoveAll(c => c is RestockWatch or ResearchQueue or CalendarHistory or BuilderPick);
        // or the calendar would start keeping again in this game
        JustDesktopMod.Settings.calendarKeep = false;
        LoadedModManager.GetMod<JustDesktopMod>().WriteSettings();
        cleared = game;
        Messages.Message("JustDesktop's data is out of this game. Save it, and it will load without the mod", MessageTypeDefOf.NeutralEvent, false);
    }

    public static void Draw(Listing_Standard list)
    {
        list.Label("Removing the mod", tooltip: "The restock letters, the research queue, the calendar's kept events and who builds what are in your save. A game saved with them shows an error once when it's loaded without the mod");
        if (!InGame)
        {
            GUI.color = Style.Dim;
            list.Label("Load the game you want to take the mod out of, then open these options from there");
            GUI.color = Color.white;
            return;
        }
        if (cleared == Current.Game || !Has(Current.Game))
        {
            GUI.color = Style.Dim;
            list.Label("Nothing of this mod is in this game anymore. Save it, then you can remove the mod");
            GUI.color = Color.white;
            return;
        }
        if (list.ButtonText("Take this mod out of the save"))
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Remove the restock letters, the research queue, the calendar's kept events and who builds what from this game? Save the game after, then you can remove the mod and load it without errors.\n\nIf you keep playing with the mod they start again empty next time the game is loaded", Clear, destructive: true));
        }
    }
}
