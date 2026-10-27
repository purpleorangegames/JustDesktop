using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JustDesktop;

// towns to watch: a button on a town that trades turns it on, and when its goods are restocked (30 days after the
// stock was last made, which happens on a visit) a letter says so, with a link to the town on the world map. Kept with
// the save
public class RestockWatch : GameComponent
{
    private List<Settlement> watched = new();
    // when each one's goods are next restocked, from the game's countdown while there is one
    private Dictionary<Settlement, int> due = new();
    private List<Settlement> dueKeys;
    private List<int> dueValues;

    public RestockWatch(Game game)
    {
    }

    public static RestockWatch Get => Current.Game?.GetComponent<RestockWatch>();

    public bool Watching(Settlement s) => watched.Contains(s);

    public void Toggle(Settlement s)
    {
        if (!watched.Remove(s))
        {
            watched.Add(s);
        }
        else
        {
            due.Remove(s);
        }
    }

    public override void GameComponentTick()
    {
        if (Find.TickManager.TicksGame % 250 != 0 || watched.Count == 0)
        {
            return;
        }
        int now = Find.TickManager.TicksGame;
        for (int i = watched.Count - 1; i >= 0; i--)
        {
            Settlement s = watched[i];
            if (s == null || s.Destroyed)
            {
                watched.RemoveAt(i);
                if (s != null)
                {
                    due.Remove(s);
                }
                continue;
            }
            // the countdown goes away the moment the old stock is dropped, so it's written down while it's there
            int next = s.trader?.NextRestockTick ?? -1;
            if (next != -1)
            {
                due[s] = next;
            }
            if (due.TryGetValue(s, out int at) && now >= at)
            {
                due.Remove(s);
                Send(s);
            }
        }
    }

    private static void Send(Settlement s)
    {
        string label = "Restocked: " + s.LabelCap;
        string text = s.LabelCap + " has new goods to trade.";
        Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.NeutralEvent, new LookTargets(s));
    }

    public override void ExposeData()
    {
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            watched.RemoveAll(s => s == null || s.Destroyed);
        }
        Scribe_Collections.Look(ref watched, "watched", LookMode.Reference);
        Scribe_Collections.Look(ref due, "due", LookMode.Reference, LookMode.Value, ref dueKeys, ref dueValues);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            watched ??= new List<Settlement>();
            watched.RemoveAll(s => s == null);
            due ??= new Dictionary<Settlement, int>();
            due.RemoveAll(kv => kv.Key == null);
        }
    }
}

// the button on a town that trades
[HarmonyPatch(typeof(Settlement), nameof(Settlement.GetGizmos))]
public static class RestockWatch_Gizmo
{
    private static Texture2D _icon;
    private static Texture2D Icon => _icon ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Hud/Restock");

    public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Settlement __instance)
    {
        foreach (Gizmo g in gizmos)
        {
            yield return g;
        }
        Settlement s = __instance;
        if (s.TraderKind == null || s.Faction == null || s.Faction.IsPlayer || s.Faction.def.permanentEnemy || RestockWatch.Get is not { } watch)
        {
            yield break;
        }
        yield return new Command_Toggle
        {
            defaultLabel = "Restock letter",
            defaultDesc = "A letter when " + s.LabelCap + " has new goods to trade, with a link to it on the world map.\n\nTowns restock 30 days after their goods were last made, which happens on your first visit and then on each visit after a restock, so the countdown starts once you've been there.",
            icon = Icon,
            isActive = () => watch.Watching(s),
            toggleAction = () => watch.Toggle(s)
        };
    }
}
