using System;
using System.Collections.Generic;
using UnityEngine;

namespace PocketRoles.Core
{
    /// <summary>Delayed actions driven from the HudManager.Update postfix (works in lobby and in game).</summary>
    public static class Scheduler
    {
        private sealed class Entry
        {
            public float DueAt;
            public Action Action;
            public string Tag;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly List<Entry> Due = new List<Entry>();

        public static void After(float seconds, Action action, string tag = null)
        {
            if (action == null) return;
            Entries.Add(new Entry { DueAt = Time.time + Mathf.Max(0f, seconds), Action = action, Tag = tag });
        }

        public static void Cancel(string tag)
        {
            if (tag == null) return;
            Entries.RemoveAll(e => e.Tag == tag);
        }

        public static bool HasTag(string tag)
        {
            if (tag == null) return false;
            foreach (var e in Entries) if (e.Tag == tag) return true;
            return false;
        }

        public static void Tick()
        {
            if (Entries.Count == 0) return;
            float now = Time.time;
            Due.Clear();
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].DueAt <= now)
                {
                    Due.Add(Entries[i]);
                    Entries.RemoveAt(i);
                }
            }
            // run in scheduling order
            for (int i = Due.Count - 1; i >= 0; i--)
            {
                try
                {
                    Due[i].Action();
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"Scheduler action '{Due[i].Tag}' failed: {e}");
                }
            }
            Due.Clear();
        }

        public static void Clear()
        {
            Entries.Clear();
        }
    }
}
