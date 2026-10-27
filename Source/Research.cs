using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JustDesktop;

// the Research window in the modern windows: the whole height of the screen, the projects as rounded cards colored
// by state with a thin progress bar and soft curved lines, a queue along the bottom (Shift+click a project to add it
// and what it still needs, drag to reorder, right click to remove) with when each one should be done, and the side
// panel laid out anew with the current project and its time left at the bottom. Tabs, search and the details of a
// project (prerequisites, benches, what it unlocks) are still the game's
public static class Research
{
    public static bool On => ModernWidgets.Active && JustDesktopMod.Settings.modernResearch;

    private const float NodeWidth = 140f;
    private const float StripHeight = 54f;
    private const float PillWidth = 180f;

    private static readonly AccessTools.FieldRef<MainTabWindow_Research, ResearchProjectDef> selected = AccessTools.FieldRefAccess<MainTabWindow_Research, ResearchProjectDef>("selectedProject");
    private static readonly AccessTools.FieldRef<MainTabWindow_Research, Vector2> rightScroll = AccessTools.FieldRefAccess<MainTabWindow_Research, Vector2>("rightScrollPosition");
    private static readonly AccessTools.FieldRef<MainTabWindow_Research, ScrollPositioner> positioner = AccessTools.FieldRefAccess<MainTabWindow_Research, ScrollPositioner>("scrollPositioner");
    private static readonly AccessTools.FieldRef<MainTabWindow_Research, QuickSearchWidget> search = AccessTools.FieldRefAccess<MainTabWindow_Research, QuickSearchWidget>("quickSearchWidget");
    private static readonly AccessTools.FieldRef<MainTabWindow_Research, HashSet<ResearchProjectDef>> matching = AccessTools.FieldRefAccess<MainTabWindow_Research, HashSet<ResearchProjectDef>>("matchingProjects");
    private static readonly Action<MainTabWindow_Research, ResearchProjectDef> attemptBegin = AccessTools.MethodDelegate<Action<MainTabWindow_Research, ResearchProjectDef>>(AccessTools.Method(typeof(MainTabWindow_Research), "AttemptBeginResearch"));
    private static readonly Action<MainTabWindow_Research, Rect> scrollView = AccessTools.MethodDelegate<Action<MainTabWindow_Research, Rect>>(AccessTools.Method(typeof(MainTabWindow_Research), "DrawProjectScrollView"));
    private static readonly Action<MainTabWindow_Research, Rect> tabInfo = AccessTools.MethodDelegate<Action<MainTabWindow_Research, Rect>>(AccessTools.Method(typeof(MainTabWindow_Research), "DrawCurrentTabInfo"));
    private delegate void TechprintInfo(MainTabWindow_Research w, Rect rect, ref float y);
    private static readonly TechprintInfo techprintInfo = AccessTools.MethodDelegate<TechprintInfo>(AccessTools.Method(typeof(MainTabWindow_Research), "DrawTechprintInfo"));

    private static Texture2D _techprintTex;

    private static Texture2D TechprintTex => _techprintTex ??= ContentFinder<Texture2D>.Get("UI/Icons/Research/Techprint");
    private static Texture2D _studyTex;
    private static Texture2D StudyTex => _studyTex ??= ContentFinder<Texture2D>.Get("UI/Icons/Study");
    private static readonly Color Done = new(0.4f, 0.75f, 0.5f, 1f);
    private static readonly Color Missing = new(0.9f, 0.4f, 0.4f, 1f);

    private static float PosX(ResearchProjectDef d) => d.ResearchViewX * 190f;
    // the game puts rows 100 apart; here they spread to fill the window's height, up to half again as far
    private static float rowSpacing = 100f;
    private static float PosY(ResearchProjectDef d) => d.ResearchViewY * rowSpacing;
    private static float Extra(ResearchProjectDef d) => ModsConfig.AnomalyActive && d.knowledgeCategory != null ? 14f : 0f;

    private static Color A(Color c, float a) => new(c.r, c.g, c.b, c.a * a);

    private static void Fill(Rect r, Color c)
    {
        GUI.color = c;
        Widgets.DrawAtlas(r, ModernWidgets.Fill);
    }

    private static void Outline(Rect r, Color c)
    {
        GUI.color = c;
        Widgets.DrawAtlas(r, ModernWidgets.Line);
    }

    // ---------------------------------------------------------------- the tree

    public static void Projects(MainTabWindow_Research w, Rect inRect)
    {
        ResearchTabDef tab = w.CurTab;
        ResearchProjectDef sel = selected(w);
        ResearchQueue queue = ResearchQueue.Get;
        bool filtering = search(w).filter.Active;
        HashSet<ResearchProjectDef> matches = matching(w);
        Vector2 scroll = rightScroll(w);
        Rect visible = new Rect(scroll.x, scroll.y, inRect.width, inRect.height).ExpandedBy(40f);
        List<ResearchProjectDef> projects = w.VisibleResearchProjects;
        float rows = 0f;
        foreach (ResearchProjectDef p in projects)
        {
            if (p.tab == tab)
            {
                rows = Mathf.Max(rows, p.ResearchViewY);
            }
        }
        rowSpacing = rows <= 0f ? 100f : Mathf.Clamp((inRect.height - 70f) / rows, 100f, 150f);

        // lines first, the ones touching the selected project over the rest
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (ResearchProjectDef p in projects)
            {
                if (p.tab != tab || p.prerequisites == null)
                {
                    continue;
                }
                Vector2 end = new(PosX(p), PosY(p) + 25f);
                foreach (ResearchProjectDef pre in p.prerequisites)
                {
                    if (pre == null || pre.tab != tab)
                    {
                        continue;
                    }
                    Vector2 start = new(PosX(pre) + NodeWidth + Extra(pre), PosY(pre) + 25f);
                    bool hot = sel != null && (sel == p || sel == pre);
                    if (hot != (pass == 1))
                    {
                        continue;
                    }
                    Rect bounds = Rect.MinMaxRect(Mathf.Min(start.x, end.x), Mathf.Min(start.y, end.y), Mathf.Max(start.x, end.x), Mathf.Max(start.y, end.y));
                    if (!bounds.ExpandedBy(4f).Overlaps(visible))
                    {
                        continue;
                    }
                    Color c = hot ? Style.Faded(0.7f) : p.IsFinished && pre.IsFinished ? A(Done, 0.3f) : new Color(1f, 1f, 1f, 0.12f);
                    if (filtering && !(matches.Contains(p) && matches.Contains(pre)))
                    {
                        c = A(c, 0.4f);
                    }
                    Curve(start, end, c, hot ? 2.5f : 2f);
                }
            }
        }

        Event e = Event.current;
        bool overCard = false;
        foreach (ResearchProjectDef project in projects)
        {
            if (project.tab != tab)
            {
                continue;
            }
            float extra = Extra(project);
            string label = project.IsHidden ? "(" + "UnknownResearch".Translate() + ")" : project.LabelCap.ToString();
            Text.Font = GameFont.Small;
            float labelHeight = Text.CalcHeight(label, NodeWidth - 12f);
            Rect rect = new Rect(PosX(project), PosY(project), NodeWidth + extra, Mathf.Max(50f, labelHeight + 26f));
            bool match = filtering && matches.Contains(project);
            if (match || sel == project)
            {
                positioner(w).RegisterInterestRect(rect);
            }
            if (!rect.Overlaps(visible))
            {
                continue;
            }
            float a = filtering && !match ? 0.35f : 1f;
            Node(w, rect, extra, label, project, sel, queue, a, match);

            if (project.IsHidden)
            {
                continue;
            }
            if (Mouse.IsOver(rect))
            {
                overCard = true;
                TooltipHandler.TipRegion(rect, () => project.GetTip() + (ResearchQueue.Queueable(project) ? "\n\n" + "Shift+click: add to the queue, with what it still needs".Colorize(ColoredText.SubtleGrayColor) : ""), project.GetHashCode() ^ 0x1664F);
                if (e.type == EventType.MouseDown && e.button == 1)
                {
                    Menu(w, project);
                    e.Use();
                }
            }
            if (Widgets.ButtonInvisible(rect) && e.button == 0)
            {
                if (e.shift && queue != null && ResearchQueue.Queueable(project))
                {
                    if (queue.Contains(project))
                    {
                        queue.Remove(project);
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    }
                    else
                    {
                        queue.Add(project);
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    }
                }
                else
                {
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    selected(w) = project;
                }
            }
        }
        // only inside the tree's own area, not on the tabs or the queue
        Pan(w, e, overCard || !Mouse.IsOver(new Rect(scroll.x - 20f, scroll.y - 20f, inRect.width + 40f, inRect.height + 20f)));
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperLeft;
    }

    // dragging the empty space between the cards (or anywhere with the middle button) moves the view
    private static bool panning;

    private static void Pan(MainTabWindow_Research w, Event e, bool notHere)
    {
        if (e.type == EventType.MouseDown && !notHere && (e.button == 2 || (e.button == 0 && !e.shift)))
        {
            panning = true;
            e.Use();
        }
        else if (panning && e.type == EventType.MouseDrag)
        {
            Vector2 scroll = rightScroll(w) - e.delta;
            rightScroll(w) = new Vector2(Mathf.Max(0f, scroll.x), Mathf.Max(0f, scroll.y));
            e.Use();
        }
        else if (panning && (e.rawType == EventType.MouseUp || (e.type == EventType.Repaint && !Input.GetMouseButton(0) && !Input.GetMouseButton(2))))
        {
            panning = false;
        }
    }

    private static void Node(MainTabWindow_Research w, Rect rect, float extra, string label, ResearchProjectDef project, ResearchProjectDef sel, ResearchQueue queue, float a, bool match)
    {
        bool current = Find.ResearchManager.IsCurrentProject(project);
        bool locked = !project.IsFinished && !project.CanStartNow;
        Color back, text = Style.Text, border = new(1f, 1f, 1f, 0.14f);
        if (project.IsHidden)
        {
            back = new Color(0f, 0f, 0f, 0.3f);
            text = Style.Disabled;
            border = new Color(1f, 1f, 1f, 0.05f);
        }
        else if (current)
        {
            back = Style.Faded(0.32f);
            text = Color.white;
            border = Style.Accent;
        }
        else if (project.IsFinished)
        {
            back = A(Done, 0.22f);
            border = A(Done, 0.35f);
        }
        else if (locked)
        {
            back = new Color(0f, 0f, 0f, 0.28f);
            text = Style.Dim;
            border = new Color(1f, 1f, 1f, 0.07f);
        }
        else
        {
            back = new Color(1f, 1f, 1f, 0.08f);
        }
        // the selected project's prerequisites and what it leads to stand out
        if (sel != null && sel != project)
        {
            if (sel.prerequisites.NotNullAndContains(project) || sel.hiddenPrerequisites.NotNullAndContains(project))
            {
                border = project.IsFinished ? A(Done, 0.8f) : Style.Faded(0.85f);
            }
            else if (project.prerequisites.NotNullAndContains(sel) || project.hiddenPrerequisites.NotNullAndContains(sel))
            {
                border = new Color(1f, 1f, 1f, 0.5f);
            }
        }
        if (match)
        {
            Fill(rect.ExpandedBy(4f), Style.Faded(0.3f));
        }
        // solid under the tint, so the lines passing behind don't show through
        Color solid = JustDesktopMod.Settings.windowColor;
        solid.a = a;
        Fill(rect, solid);
        Fill(rect, A(back, a));
        if (Mouse.IsOver(rect) && !project.IsHidden)
        {
            Fill(rect, new Color(1f, 1f, 1f, 0.06f));
        }
        Outline(rect, A(border, a));
        if (sel == project)
        {
            Outline(rect, new Color(1f, 1f, 1f, 0.9f * a));
            Outline(rect.ContractedBy(1f), new Color(1f, 1f, 1f, 0.5f * a));
        }
        else if (current)
        {
            Outline(rect.ContractedBy(1f), A(border, 0.5f * a));
        }

        // Anomaly's category marker
        if (extra > 0f)
        {
            GUI.color = A(project.knowledgeCategory.color, a);
            GUI.DrawTexture(new Rect(rect.x + 4f, rect.y, 14f, rect.height), project.knowledgeCategory.Tex, ScaleMode.ScaleToFit);
        }
        Rect inner = new Rect(rect.x + extra + 6f, rect.y + 4f, rect.width - extra - 12f, rect.height - 8f);
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = A(text, a);
        Widgets.Label(new Rect(inner.x, inner.y, inner.width, inner.height - 16f), label);

        if (!project.IsHidden)
        {
            Rect bottom = new Rect(inner.x, inner.yMax - 16f, inner.width, 16f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = A(project.IsFinished ? A(Done, 0.9f) : Style.Dim, a);
            Widgets.Label(bottom, project.IsFinished ? "Finished".Translate().ToString() : project.CostApparent.ToString("F0"));
            float x = bottom.xMax;
            if (project.RequiredAnalyzedThingCount > 0)
            {
                x = Requirement(x, bottom, StudyTex, project.AnalyzedThingsCompleted, project.RequiredAnalyzedThingCount, project.AnalyzedThingsRequirementsMet, a);
            }
            if (project.TechprintCount > 0)
            {
                Requirement(x, bottom, TechprintTex, project.TechprintsApplied, project.TechprintCount, project.TechprintRequirementMet, a);
            }
            // a thin bar along the bottom while it's partly done
            float progress = project.ProgressPercent;
            if (progress > 0f && progress < 1f)
            {
                Rect bar = new Rect(rect.x + extra + 6f, rect.yMax - 4f, rect.width - extra - 12f, 2f);
                GUI.color = new Color(1f, 1f, 1f, 0.12f * a);
                GUI.DrawTexture(bar, BaseContent.WhiteTex);
                GUI.color = A(current ? Style.Accent : new Color(1f, 1f, 1f, 0.6f), a);
                GUI.DrawTexture(new Rect(bar.x, bar.y, Mathf.Round(bar.width * progress), bar.height), BaseContent.WhiteTex);
            }
        }

        // its place in the queue, on the corner
        int index = queue?.IndexOf(project) ?? -1;
        if (index >= 0)
        {
            Badge(new Rect(rect.xMax - 13f, rect.y - 7f, 20f, 20f), (index + 1).ToString(), a);
        }
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static float Requirement(float right, Rect row, Texture2D icon, int have, int need, bool met, float a)
    {
        string s = have + "/" + need;
        float w = Text.CalcSize(s).x;
        Text.Anchor = TextAnchor.MiddleRight;
        GUI.color = A(met ? Done : Missing, a);
        Widgets.Label(new Rect(right - w, row.y, w, row.height), s);
        GUI.color = new Color(1f, 1f, 1f, a);
        GUI.DrawTexture(new Rect(right - w - 15f, row.y + 1f, 14f, 14f), icon);
        Text.Anchor = TextAnchor.MiddleLeft;
        return right - w - 21f;
    }

    private static void Badge(Rect r, string text, float a)
    {
        GUI.color = A(Style.Accent, a);
        GUI.DrawTexture(r, Style.Circle);
        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.MiddleCenter;
        GUI.color = new Color(1f, 1f, 1f, a);
        Widgets.Label(new Rect(r.x - 4f, r.y, r.width + 8f, r.height), text);
        Text.Anchor = TextAnchor.UpperLeft;
    }

    // a soft S curve from the right of the prerequisite to the left of what needs it
    private static void Curve(Vector2 from, Vector2 to, Color color, float width)
    {
        if (Mathf.Abs(from.y - to.y) < 1f)
        {
            Widgets.DrawLine(from, to, color, width);
            return;
        }
        float dx = Mathf.Max(30f, Mathf.Abs(to.x - from.x) * 0.5f);
        Vector2 c1 = from + new Vector2(dx, 0f), c2 = to - new Vector2(dx, 0f);
        const int Steps = 12;
        Vector2 prev = from;
        for (int i = 1; i <= Steps; i++)
        {
            float t = i / (float)Steps, u = 1f - t;
            Vector2 p = u * u * u * from + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * to;
            Widgets.DrawLine(prev, p, color, width);
            prev = p;
        }
    }

    private static void Menu(MainTabWindow_Research w, ResearchProjectDef p)
    {
        List<FloatMenuOption> options = new();
        ResearchQueue queue = ResearchQueue.Get;
        if (p.CanStartNow && !Find.ResearchManager.IsCurrentProject(p))
        {
            options.Add(new FloatMenuOption("Research".Translate(), () => attemptBegin(w, p)));
        }
        if (Find.ResearchManager.IsCurrentProject(p))
        {
            options.Add(new FloatMenuOption("StopResearch".Translate(), () => Find.ResearchManager.StopProject(p)));
        }
        if (queue != null && ResearchQueue.Queueable(p))
        {
            options.Add(queue.Contains(p)
                ? new FloatMenuOption("Remove from the queue", () => queue.Remove(p))
                : new FloatMenuOption("Add to the queue", () => queue.Add(p)));
        }
        options.Add(new FloatMenuOption("Select", () => selected(w) = p));
        Find.WindowStack.Add(new FloatMenu(options));
    }

    // ---------------------------------------------------------------- the queue along the bottom

    private static int dragging = -1;
    private static bool dragMoved;
    private static float dragFrom;
    private static Vector2 queueScroll;

    public static Rect Strip(Rect right) => new(right.x, right.yMax - StripHeight, right.width, StripHeight);
    public static Rect AboveStrip(Rect right) => new(right.x, right.y, right.width, right.height - StripHeight - 8f);

    public static void Queue(MainTabWindow_Research w, Rect rect)
    {
        ResearchQueue queue = ResearchQueue.Get;
        if (queue == null)
        {
            return;
        }
        Fill(rect, new Color(0f, 0f, 0f, 0.2f));
        Outline(rect, Style.Border);
        List<(ResearchProjectDef project, int ticks)> schedule = queue.Schedule();

        // title and the whole queue's time on the left
        Rect head = new Rect(rect.x + 12f, rect.y + 6f, 110f, rect.height - 12f);
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Style.Text;
        Widgets.Label(new Rect(head.x, head.y, head.width, 22f), "Queue");
        Text.Font = GameFont.Tiny;
        GUI.color = Style.Dim;
        string all = queue.queue.Count == 0 ? "empty" : queue.queue.Count + " · " + ResearchQueue.Period(schedule.Count > 0 ? schedule[schedule.Count - 1].ticks : -1);
        Widgets.Label(new Rect(head.x, head.y + 22f, head.width, 18f), all);
        TooltipHandler.TipRegion(head, "How fast the colony researched over the last day decides the times, so sleep, work and schedules count");

        Rect area = new Rect(head.xMax + 6f, rect.y + 6f, rect.xMax - head.xMax - 12f, rect.height - 12f);
        if (queue.queue.Count == 0)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Style.Dim;
            Text.Font = GameFont.Small;
            Widgets.Label(area, "Shift+click a project to add it, and what it still needs, to the queue. Drag to reorder, right click to remove");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            dragging = -1;
            return;
        }

        int offset = schedule.Count - queue.queue.Count;
        float pill = Mathf.Min(PillWidth, Mathf.Max(120f, (area.width - (queue.queue.Count - 1) * 6f) / queue.queue.Count));
        float viewWidth = queue.queue.Count * (pill + 6f) - 6f;
        Rect view = new Rect(0f, 0f, Mathf.Max(viewWidth, area.width), area.height - (viewWidth > area.width ? 10f : 0f));
        Widgets.ScrollHorizontal(area, ref queueScroll, view);
        Widgets.BeginScrollView(area, ref queueScroll, view);
        Event e = Event.current;
        int remove = -1;
        for (int i = 0; i < queue.queue.Count; i++)
        {
            ResearchProjectDef p = queue.queue[i];
            Rect r = new Rect(i * (pill + 6f), 0f, pill, view.height);
            if (dragging == i && dragMoved)
            {
                Fill(r, new Color(1f, 1f, 1f, 0.03f));
                continue;
            }
            Pill(r, p, i, schedule[i + offset].ticks, selected(w) == p);
            if (Mouse.IsOver(r))
            {
                TooltipHandler.TipRegion(r, p.LabelCap + (p.CanStartNow ? "" : "\n" + "Locked".Translate().Colorize(ColoredText.SubtleGrayColor)) + "\n\n" + "Drag to reorder, right click to remove".Colorize(ColoredText.SubtleGrayColor));
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    dragging = i;
                    dragMoved = false;
                    dragFrom = e.mousePosition.x;
                    e.Use();
                }
                else if (e.type == EventType.MouseDown && e.button == 1)
                {
                    remove = i;
                    e.Use();
                }
            }
        }
        if (dragging >= 0 && dragging < queue.queue.Count)
        {
            float mouse = e.mousePosition.x;
            if (e.type == EventType.MouseDrag)
            {
                dragMoved |= Mathf.Abs(mouse - dragFrom) > 5f;
                e.Use();
            }
            // where it would go: in front of the first one whose middle is right of the mouse
            int target = 0;
            for (int j = 0; j < queue.queue.Count; j++)
            {
                if (j != dragging && j * (pill + 6f) + pill / 2f < mouse)
                {
                    target++;
                }
            }
            if (dragMoved)
            {
                // the one being dragged follows the mouse, a line marks where it lands
                int slot = target >= dragging ? target + 1 : target;
                GUI.color = Style.Accent;
                GUI.DrawTexture(new Rect(slot * (pill + 6f) - 4f, 0f, 2f, view.height), BaseContent.WhiteTex);
                Rect ghost = new Rect(mouse - pill / 2f, 0f, pill, view.height);
                Pill(ghost, queue.queue[dragging], dragging, schedule[dragging + offset].ticks, true);
            }
            if (e.rawType == EventType.MouseUp || (e.type == EventType.Repaint && !Input.GetMouseButton(0)))
            {
                if (dragMoved)
                {
                    queue.Move(dragging, target);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
                else if (e.rawType == EventType.MouseUp)
                {
                    ResearchProjectDef p = queue.queue[dragging];
                    selected(w) = p;
                    if (p.tab != null)
                    {
                        w.CurTab = p.tab;
                    }
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                dragging = -1;
                if (e.rawType == EventType.MouseUp)
                {
                    e.Use();
                }
            }
        }
        Widgets.EndScrollView();
        if (remove >= 0)
        {
            queue.Remove(queue.queue[remove]);
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static void Pill(Rect r, ResearchProjectDef p, int index, int ticks, bool on)
    {
        bool current = Find.ResearchManager.IsCurrentProject(p);
        bool ready = p.CanStartNow;
        Fill(r, current ? Style.Faded(0.3f) : on ? new Color(1f, 1f, 1f, 0.14f) : Mouse.IsOver(r) ? new Color(1f, 1f, 1f, 0.11f) : new Color(1f, 1f, 1f, 0.07f));
        if (on)
        {
            Outline(r, new Color(1f, 1f, 1f, 0.6f));
        }
        Badge(new Rect(r.x + 7f, Mathf.Round(r.center.y - 10f), 20f, 20f), (index + 1).ToString(), ready || current ? 1f : 0.45f);
        Rect text = new Rect(r.x + 33f, r.y + 2f, r.width - 39f, r.height - 4f);
        Text.Font = GameFont.Small;
        Text.WordWrap = false;
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = ready || current ? Style.Text : Style.Dim;
        Widgets.Label(new Rect(text.x, text.y, text.width, 22f), p.LabelCap.Truncate(text.width));
        Text.Font = GameFont.Tiny;
        GUI.color = Style.Dim;
        Widgets.Label(new Rect(text.x, text.y + 20f, text.width, 16f), current ? "≈ " + ResearchQueue.Period(ticks) : ready ? "+ " + ResearchQueue.Period(ticks) : "Locked".Translate().ToString());
        Text.WordWrap = true;
        // the progress so far
        if (p.ProgressPercent > 0f)
        {
            Rect bar = new Rect(r.x + 33f, r.yMax - 5f, r.width - 41f, 2f);
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(bar, BaseContent.WhiteTex);
            GUI.color = current ? Style.Accent : new Color(1f, 1f, 1f, 0.6f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, Mathf.Round(bar.width * p.ProgressPercent), bar.height), BaseContent.WhiteTex);
        }
        GUI.color = Color.white;
    }

    // ---------------------------------------------------------------- the side panel

    public static void Left(MainTabWindow_Research w, Rect outRect)
    {
        Widgets.BeginGroup(outRect);
        Rect rect = outRect.AtZero();
        bool anomaly = ModsConfig.AnomalyActive && w.CurTab == ResearchTabDefOf.Anomaly;
        float activeHeight = anomaly ? 150f : 104f;
        Rect active = new Rect(0f, rect.yMax - activeHeight, rect.width, activeHeight);
        Rect top = new Rect(0f, 0f, rect.width, active.y - 10f);
        ResearchProjectDef p = selected(w);
        if (p != null)
        {
            Info(w, top, p);
        }
        else
        {
            tabInfo(w, top);
        }
        Active(active, anomaly);
        Widgets.EndGroup();
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static readonly List<string> reasons = new();

    private static void Info(MainTabWindow_Research w, Rect rect, ResearchProjectDef p)
    {
        float y = rect.y;
        Text.Font = GameFont.Medium;
        GUI.color = Color.white;
        float h = Text.CalcHeight(p.LabelCap, rect.width);
        Widgets.Label(new Rect(rect.x, y, rect.width, h), p.LabelCap);
        y += h + 4f;

        // what state it's in, its cost and place in the queue as pills
        Text.Font = GameFont.Tiny;
        ResearchQueue queue = ResearchQueue.Get;
        bool current = Find.ResearchManager.IsCurrentProject(p);
        (string state, Color color) = p.IsFinished ? ("Finished".Translate().ToString(), Done)
            : current ? ("InProgress".Translate().ToString(), Style.Accent)
            : p.CanStartNow ? ("Available", new Color(1f, 1f, 1f, 0.6f))
            : ("Locked".Translate().ToString(), Missing);
        float x = rect.x;
        x = Tag(x, y, state, color);
        if (!p.IsFinished && p.Cost > 0f)
        {
            x = Tag(x, y, p.ProgressApparentString + " / " + p.CostApparent.ToString("F0"), new Color(1f, 1f, 1f, 0.35f));
        }
        int index = queue?.IndexOf(p) ?? -1;
        if (index >= 0)
        {
            Tag(x, y, "Queued #" + (index + 1), Style.Faded(0.8f));
        }
        if (Prefs.DevMode && !p.IsFinished && Widgets.ButtonText(new Rect(rect.xMax - 90f, y, 90f, 20f), "Dev: finish"))
        {
            Find.ResearchManager.SetCurrentProject(p);
            Find.ResearchManager.FinishProject(p);
        }
        y += 28f;

        // what you can do with it
        Text.Font = GameFont.Small;
        float bx = rect.x;
        const float ButtonHeight = 32f;
        float bw = Mathf.Floor((rect.width - 8f) / 2f);
        if (p.CanStartNow && !current)
        {
            if (MainButton(new Rect(bx, y, bw, ButtonHeight), "Research".Translate(), true))
            {
                attemptBegin(w, p);
            }
            bx += bw + 8f;
        }
        else if (current)
        {
            if (MainButton(new Rect(bx, y, bw, ButtonHeight), "StopResearch".Translate(), false))
            {
                Find.ResearchManager.StopProject(p);
            }
            bx += bw + 8f;
        }
        if (queue != null && ResearchQueue.Queueable(p))
        {
            bool queued = queue.Contains(p);
            if (MainButton(new Rect(bx, y, bw, ButtonHeight), queued ? "Remove from queue" : "Add to queue", false))
            {
                if (queued)
                {
                    queue.Remove(p);
                }
                else
                {
                    queue.Add(p);
                }
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            bx += bw + 8f;
        }
        if (bx > rect.x)
        {
            y += ButtonHeight + 8f;
        }

        // why it can't start
        if (!p.IsFinished && !p.CanStartNow && !current)
        {
            reasons.Clear();
            if (!p.PrerequisitesCompleted)
            {
                reasons.Add("PrerequisitesNotCompleted".Translate());
            }
            if (!p.TechprintRequirementMet)
            {
                reasons.Add("InsufficientTechprintsApplied".Translate(p.TechprintsApplied, p.TechprintCount));
            }
            if (p.knowledgeCategory == null && !p.PlayerHasAnyAppropriateResearchBench)
            {
                reasons.Add("MissingRequiredResearchFacilities".Translate());
            }
            if (!p.PlayerMechanitorRequirementMet)
            {
                reasons.Add("MissingRequiredMechanitor".Translate());
            }
            if (!p.InspectionRequirementsMet)
            {
                reasons.Add("MissingGravEngineInspection".Translate());
            }
            if (!p.AnalyzedThingsRequirementsMet)
            {
                foreach (ThingDef t in p.requiredAnalyzed)
                {
                    reasons.Add("NotStudied".Translate(t.LabelCap));
                }
            }
            if (reasons.Count > 0)
            {
                string s = reasons.ToLineList("• ");
                Text.Font = GameFont.Small;
                float rh = Text.CalcHeight(s, rect.width - 16f);
                Rect box = new Rect(rect.x, y, rect.width, rh + 12f);
                Fill(box, A(Missing, 0.12f));
                GUI.color = new Color(1f, 0.8f, 0.8f, 1f);
                Widgets.Label(box.ContractedBy(8f, 6f), s);
                y += box.height + 8f;
            }
        }

        // its description
        Text.Font = GameFont.Small;
        GUI.color = Style.Text;
        string description = p.Description;
        if (ModsConfig.AnomalyActive && p.knowledgeCategory != null)
        {
            description += "\n\n" + "AnomalyResearchDescriptionHelpText".Translate().Colorize(ColoredText.SubtleGrayColor);
            description += "\n" + "KnowledgeCategory".Translate() + ": " + p.knowledgeCategory.LabelCap;
        }
        float dh = Text.CalcHeight(description, rect.width);
        Widgets.Label(new Rect(rect.x, y, rect.width, dh), description);
        y += dh + 8f;
        GUI.color = Color.white;
        techprintInfo(w, new Rect(rect.x, y, rect.width, 500f), ref y);
        GUI.color = Style.Divider;
        GUI.DrawTexture(new Rect(rect.x, y + 4f, rect.width, 1f), BaseContent.WhiteTex);
        GUI.color = Color.white;
        y += 10f;

        // prerequisites, benches, what it unlocks: the game's
        if (rect.yMax - y > 40f)
        {
            scrollView(w, new Rect(rect.x, y, rect.width, rect.yMax - y));
        }
    }

    private static float Tag(float x, float y, string text, Color color)
    {
        float w = Text.CalcSize(text).x + 14f;
        Rect r = new Rect(x, y, w, 20f);
        Fill(r, A(color, 0.2f));
        GUI.color = Color.Lerp(color, Color.white, 0.4f);
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(r, text);
        Text.Anchor = TextAnchor.UpperLeft;
        return r.xMax + 6f;
    }

    private static bool MainButton(Rect r, string label, bool accent)
    {
        bool over = Mouse.IsOver(r);
        Fill(r, accent ? Style.Faded(over ? 0.75f : 0.55f) : over ? new Color(1f, 1f, 1f, 0.15f) : new Color(1f, 1f, 1f, 0.08f));
        Outline(r, accent ? Style.Faded(0.9f) : Style.Border);
        GUI.color = accent || over ? Color.white : Style.Text;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(r, label);
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        return Widgets.ButtonInvisible(r);
    }

    // the project being researched, how far and when it should be done
    private static void Active(Rect rect, bool anomaly)
    {
        Fill(rect, new Color(0f, 0f, 0f, 0.2f));
        Outline(rect, Style.Border);
        Rect inner = rect.ContractedBy(12f, 10f);
        Text.Font = GameFont.Tiny;
        GUI.color = Style.Dim;
        Widgets.Label(new Rect(inner.x, inner.y, inner.width, 16f), (anomaly ? "ActiveProjectPlural" : "ActiveProject").Translate());
        float y = inner.y + 18f;
        if (anomaly)
        {
            foreach (KnowledgeCategoryDef cat in new[] { KnowledgeCategoryDefOf.Basic, KnowledgeCategoryDefOf.Advanced })
            {
                Progress(new Rect(inner.x, y, inner.width, 52f), Find.ResearchManager.GetProject(cat), cat.LabelCap + ": ", -2);
                y += 56f;
            }
        }
        else
        {
            ResearchProjectDef p = Find.ResearchManager.GetProject();
            ResearchQueue queue = ResearchQueue.Get;
            Progress(new Rect(inner.x, y, inner.width, 66f), p, "", p == null || queue == null ? -2 : queue.TicksFor(ResearchQueue.Left(p)));
        }
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static void Progress(Rect rect, ResearchProjectDef p, string prefix, int ticks)
    {
        Text.Font = GameFont.Small;
        Text.WordWrap = false;
        if (p == null)
        {
            GUI.color = Style.Dim;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), prefix + "NoProjectSelected".Translate());
            Text.WordWrap = true;
            return;
        }
        GUI.color = Color.white;
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), (prefix + p.LabelCap).Truncate(rect.width));
        Text.WordWrap = true;
        Text.Font = GameFont.Small;
        Pane.DrawBar(new Rect(rect.x, rect.y + 25f, rect.width, 20f), new Pane.Bar
        {
            Label = p.ProgressPercent.ToStringPercent(),
            Value = p.ProgressPercent,
            Color = Style.Accent,
            ValueText = p.ProgressApparentString + " / " + p.CostApparent.ToString("F0"),
            Tip = p.LabelCap
        }, null);
        if (ticks > -2)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Style.Dim;
            Widgets.Label(new Rect(rect.x, rect.y + 47f, rect.width, 18f), ticks < 0 ? "Time left: measuring how fast the colony researches…" : "Time left: about " + ResearchQueue.Period(ticks));
        }
    }
}

// as tall as the screen allows
[HarmonyPatch(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.InitialSize), MethodType.Getter)]
public static class Research_Size
{
    public static void Postfix(ref Vector2 __result)
    {
        if (ModernWidgets.Enabled && JustDesktopMod.Settings.modernResearch)
        {
            __result.y = UI.screenHeight;
        }
    }
}

[HarmonyPatch(typeof(MainTabWindow_Research), "ListProjects")]
public static class Research_Projects
{
    public static bool Prefix(MainTabWindow_Research __instance, Rect rightInRect, bool ___editMode)
    {
        if (!Research.On || ___editMode)
        {
            return true;
        }
        Research.Projects(__instance, rightInRect);
        return false;
    }
}

// the queue takes the bottom of the right side
[HarmonyPatch(typeof(MainTabWindow_Research), "DrawRightRect")]
public static class Research_Right
{
    public static void Prefix(MainTabWindow_Research __instance, ref Rect rightOutRect)
    {
        if (!Research.On)
        {
            return;
        }
        Research.Queue(__instance, Research.Strip(rightOutRect));
        rightOutRect = Research.AboveStrip(rightOutRect);
    }
}

[HarmonyPatch(typeof(MainTabWindow_Research), "DrawLeftRect")]
public static class Research_Left
{
    public static bool Prefix(MainTabWindow_Research __instance, Rect leftOutRect)
    {
        if (!Research.On)
        {
            return true;
        }
        Research.Left(__instance, leftOutRect);
        return false;
    }
}
