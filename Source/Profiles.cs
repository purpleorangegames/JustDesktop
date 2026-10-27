using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// all of this mod's settings saved under a name, one file each in the save data folder, to load again later. The
// suggested ones come with the mod (its Profiles folder), they can be loaded but not saved over or deleted
public static class Profiles
{
    public static string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath, "JustDesktop", "Profiles");
    private static string SuggestedFolder => Path.Combine(LoadedModManager.GetMod<JustDesktopMod>().Content.RootDir, "Profiles");

    private static string name = "";
    private static List<string> names, suggested;

    private static string PathOf(string profile) => Path.Combine(Folder, profile + ".xml");

    private static List<string> InFolder(string folder)
    {
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.xml").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
    }

    private static List<string> Names => names ??= InFolder(Folder);
    // the suggested ones in this order, any others after
    private static readonly string[] order = { "Default", "Alternative", "Small dock", "Taskbar" };
    private static List<string> Suggested => suggested ??= InFolder(SuggestedFolder).OrderBy(n => Array.IndexOf(order, n) is var i && i >= 0 ? i : order.Length).ToList();

    private static void Save(string profile)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Scribe.saver.InitSaving(PathOf(profile), "JustDesktopProfile");
            JustDesktopMod.Settings.ExposeData();
            Scribe.saver.FinalizeSaving();
            Messages.Message("Saved the settings as " + profile, MessageTypeDefOf.SilentInput, false);
        }
        catch (Exception e)
        {
            Scribe.ForceStop();
            Log.Error("[JustDesktop] couldn't save profile " + profile + ": " + e);
        }
        names = null;
    }

    private static void Load(string profile, string path)
    {
        try
        {
            // straight into the settings in use; anything the file doesn't have goes back to its default, except the
            // picked icons, hidden resources and Architect favorites, which stay as they are when it has none
            Settings set = JustDesktopMod.Settings;
            var kept = (set.hiddenResources, set.architectFavorites, set.architectRecent, set.taskbarIcons, set.dockIcons);
            string text = File.ReadAllText(path);
            Scribe.loader.InitLoading(path);
            set.ExposeData();
            Scribe.loader.FinalizeLoading();
            if (!text.Contains("<hiddenResources>"))
            {
                set.hiddenResources = kept.hiddenResources;
            }
            if (!text.Contains("<architectFavorites>"))
            {
                set.architectFavorites = kept.architectFavorites;
            }
            if (!text.Contains("<architectRecent>"))
            {
                set.architectRecent = kept.architectRecent;
            }
            if (!text.Contains("<taskbarIcons>"))
            {
                set.taskbarIcons = kept.taskbarIcons;
            }
            if (!text.Contains("<dockIcons>"))
            {
                set.dockIcons = kept.dockIcons;
            }
            Style.ClearCache();
            LoadedModManager.GetMod<JustDesktopMod>().WriteSettings();
            Messages.Message("Loaded " + profile, MessageTypeDefOf.SilentInput, false);
        }
        catch (Exception e)
        {
            Scribe.ForceStop();
            Log.Error("[JustDesktop] couldn't load profile " + profile + ": " + e);
        }
    }

    private static void Delete(string profile)
    {
        try
        {
            File.Delete(PathOf(profile));
        }
        catch (Exception e)
        {
            Log.Error("[JustDesktop] couldn't delete profile " + profile + ": " + e);
        }
        names = null;
    }

    // every setting back to what the mod starts with, icons and favorites included
    private static void ResetAll()
    {
        var fresh = new Settings();
        foreach (FieldInfo field in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            field.SetValue(JustDesktopMod.Settings, field.GetValue(fresh));
        }
        Style.ClearCache();
        LoadedModManager.GetMod<JustDesktopMod>().WriteSettings();
        Messages.Message("All the settings are back to their defaults", MessageTypeDefOf.SilentInput, false);
    }

    private const float ButtonW = 64f;

    public static void Draw(Listing_Standard list)
    {
        Settings set = JustDesktopMod.Settings;
        list.Label("Save all the settings of this mod under a name, to come back to them later or switch between setups");
        list.Gap(6f);

        Rect row = list.GetRect(30f);
        name = Widgets.TextField(new Rect(row.x, row.y, row.width - ButtonW * 2f - 10f, row.height), name);
        string trimmed = name.Trim();
        bool valid = trimmed.Length > 0 && GenText.IsValidFilename(trimmed);
        if (Widgets.ButtonText(new Rect(row.xMax - ButtonW * 2f - 4f, row.y, ButtonW * 2f + 4f, row.height), "Save as new", active: valid) && valid)
        {
            SaveAsking(trimmed);
        }
        if (trimmed.Length > 0 && !valid)
        {
            GUI.color = Style.Dim;
            list.Label("That name has characters a file name can't have");
            GUI.color = Color.white;
        }

        list.Gap(12f);
        if (Names.Count == 0)
        {
            GUI.color = Style.Dim;
            list.Label("No saved profiles yet");
            GUI.color = Color.white;
        }
        foreach (string profile in Names.ToList())
        {
            Rect r = Row(list, profile, 3);
            if (Widgets.ButtonText(Button(r, 2), "Save"))
            {
                SaveAsking(profile);
            }
            LoadButton(Button(r, 1), profile, PathOf(profile));
            if (Widgets.ButtonText(Button(r, 0), "Delete"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Delete " + profile + "?", () => Delete(profile), destructive: true));
            }
        }

        list.Gap(12f);
        bool show = !set.hideSuggested;
        list.CheckboxLabeled("Show the suggested profiles", ref show, "Setups that come with the mod, to try. They can be loaded but not changed or deleted");
        set.hideSuggested = !show;
        if (show)
        {
            foreach (string profile in Suggested)
            {
                Rect r = Row(list, profile, 1);
                LoadButton(Button(r, 0), profile, Path.Combine(SuggestedFolder, profile + ".xml"));
            }
        }

        list.Gap(12f);
        if (list.ButtonText("Open the profiles folder"))
        {
            Directory.CreateDirectory(Folder);
            Dialog_Icons.OpenFolder(Folder);
        }
        list.GapLine(20f);
        if (list.ButtonText("Reset all settings to defaults"))
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Put every setting of this mod back to its default, the picked icons and Architect favorites too? Save a profile first to keep what you have now", ResetAll, destructive: true));
        }
        list.GapLine(20f);
        SaveCleanup.Draw(list);
    }

    // a profile's row with its name and room for that many buttons at the right
    private static Rect Row(Listing_Standard list, string profile, int buttons)
    {
        Rect r = list.GetRect(30f);
        if (Mouse.IsOver(r))
        {
            Widgets.DrawHighlight(r);
        }
        float labelW = r.width - buttons * (ButtonW + 4f) - 10f;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(r.x + 6f, r.y, labelW, r.height), profile.Truncate(labelW));
        Text.Anchor = TextAnchor.UpperLeft;
        list.Gap(4f);
        return r;
    }

    // the i-th button from the right
    private static Rect Button(Rect row, int i) => new(row.xMax - (i + 1) * ButtonW - i * 4f, row.y, ButtonW, row.height);

    private static void LoadButton(Rect r, string profile, string path)
    {
        if (Widgets.ButtonText(r, "Load"))
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Load " + profile + "? The settings you have now are replaced, save them first to keep them", () => Load(profile, path)));
        }
    }

    private static void SaveAsking(string profile)
    {
        if (Names.Contains(profile, StringComparer.OrdinalIgnoreCase))
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Replace " + profile + " with the settings you have now?", () => Save(profile)));
        }
        else
        {
            Save(profile);
        }
    }
}
