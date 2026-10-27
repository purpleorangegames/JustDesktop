using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// the projects to research next, kept in the save. When one is finished the first of the rest that can start
// becomes the current project. Anomaly's projects aren't queued, they have their own slots. How fast the colony
// researches is measured over the last day (sleep, work and schedules included) for the time left
public class ResearchQueue : GameComponent
{
    public List<ResearchProjectDef> queue = new();

    // research gained in each of the last hours, the newest last
    private List<float> hours = new();
    private float gained;
    private const int Hour = GenDate.TicksPerHour;
    private const int Day = 24;

    public ResearchQueue(Game game)
    {
    }

    public static ResearchQueue Get => Current.Game?.GetComponent<ResearchQueue>();

    public static bool Queueable(ResearchProjectDef p) => p != null && !p.IsFinished && p.knowledgeCategory == null && !p.IsHidden;

    public bool Contains(ResearchProjectDef p) => queue.Contains(p);

    public int IndexOf(ResearchProjectDef p) => queue.IndexOf(p);

    // the project and what it still needs first, prerequisites before what depends on them
    public void Add(ResearchProjectDef p)
    {
        if (!Queueable(p) || queue.Contains(p))
        {
            return;
        }
        foreach (ResearchProjectDef pre in Missing(p))
        {
            if (!queue.Contains(pre))
            {
                queue.Add(pre);
            }
        }
        queue.Add(p);
        Sync();
    }

    private static IEnumerable<ResearchProjectDef> Missing(ResearchProjectDef p)
    {
        List<ResearchProjectDef> order = new();
        HashSet<ResearchProjectDef> seen = new();
        void Visit(ResearchProjectDef d)
        {
            if (d.prerequisites == null)
            {
                return;
            }
            foreach (ResearchProjectDef pre in d.prerequisites)
            {
                if (pre != null && Queueable(pre) && seen.Add(pre))
                {
                    Visit(pre);
                    order.Add(pre);
                }
            }
        }
        Visit(p);
        return order;
    }

    public void Remove(ResearchProjectDef p)
    {
        if (queue.Remove(p))
        {
            Sync();
        }
    }

    public void Move(int from, int to)
    {
        if (from < 0 || from >= queue.Count)
        {
            return;
        }
        ResearchProjectDef p = queue[from];
        queue.RemoveAt(from);
        queue.Insert(Mathf.Clamp(to, 0, queue.Count), p);
        Sync();
    }

    // the first queued project that can start becomes the current one, unless one was started outside the queue
    public void Sync()
    {
        queue.RemoveAll(p => p == null || p.IsFinished);
        ResearchManager rm = Find.ResearchManager;
        if (queue.Count == 0 || rm == null)
        {
            return;
        }
        ResearchProjectDef current = rm.GetProject();
        if (current != null && !queue.Contains(current))
        {
            return;
        }
        ResearchProjectDef first = queue.FirstOrDefault(p => p.CanStartNow);
        if (first != null && first != current)
        {
            rm.SetCurrentProject(first);
        }
    }

    public void Gained(float amount)
    {
        gained += amount;
    }

    public override void GameComponentTick()
    {
        if (Find.TickManager.TicksGame % Hour != 0)
        {
            return;
        }
        // hours with nothing to research don't count
        if (gained > 0f || Find.ResearchManager.GetProject() != null)
        {
            hours.Add(gained);
            if (hours.Count > Day)
            {
                hours.RemoveAt(0);
            }
        }
        gained = 0f;
    }

    // research per tick, averaged over the hours measured
    public float Rate => hours.Count == 0 ? 0f : hours.Sum() / (hours.Count * (float)Hour);

    // ticks to finish this much research, -1 when there's no telling
    public int TicksFor(float research)
    {
        float rate = Rate;
        return rate <= 0f ? -1 : Mathf.CeilToInt(research / rate);
    }

    public static float Left(ResearchProjectDef p) => Mathf.Max(0f, p.Cost - p.ProgressReal);

    public static string Period(int ticks) => ticks < 0 ? "?" : ticks.ToStringTicksToPeriod(allowSeconds: false, shortForm: true);

    // the current project first if it isn't queued, then the queue in order: when each one would be done
    public List<(ResearchProjectDef project, int ticks)> Schedule()
    {
        List<(ResearchProjectDef, int)> list = new();
        float sum = 0f;
        float rate = Rate;
        ResearchProjectDef current = Find.ResearchManager.GetProject();
        if (current != null && !queue.Contains(current))
        {
            sum += Left(current);
            list.Add((current, rate <= 0f ? -1 : Mathf.CeilToInt(sum / rate)));
        }
        foreach (ResearchProjectDef p in queue)
        {
            sum += Left(p);
            list.Add((p, rate <= 0f ? -1 : Mathf.CeilToInt(sum / rate)));
        }
        return list;
    }

    public override void ExposeData()
    {
        Scribe_Collections.Look(ref queue, "queue", LookMode.Def);
        Scribe_Collections.Look(ref hours, "hours", LookMode.Value);
        Scribe_Values.Look(ref gained, "gained");
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            queue ??= new List<ResearchProjectDef>();
            hours ??= new List<float>();
            queue.RemoveAll(p => p == null);
        }
    }
}

// how much each bit of research really added, whoever or whatever did it
[HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.ResearchPerformed))]
public static class ResearchQueue_Performed
{
    public static void Prefix(ResearchManager __instance, out (ResearchProjectDef, float) __state)
    {
        ResearchProjectDef p = __instance.GetProject();
        __state = (p, p == null ? 0f : __instance.GetProgress(p));
    }

    public static void Postfix(ResearchManager __instance, (ResearchProjectDef p, float before) __state)
    {
        if (__state.p != null)
        {
            ResearchQueue.Get?.Gained(Mathf.Max(0f, __instance.GetProgress(__state.p) - __state.before));
        }
    }
}

// a finished project leaves the queue and the next one starts
[HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
public static class ResearchQueue_Finish
{
    public static void Postfix(ResearchProjectDef proj)
    {
        if (proj.knowledgeCategory == null && Find.ResearchManager != null)
        {
            ResearchQueue.Get?.Sync();
        }
    }
}
