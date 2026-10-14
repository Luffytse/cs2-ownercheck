// Finds objects whose Owner points to an entity that no longer exists - the exact
// condition that crashes Game.Objects.SubObjectReferencesSystem+UpdateSubObjectReferencesJob.
//
//  * On save load: read-only scan of every owned entity, logs dead owners.
//  * Every frame (in several update phases): checks only Created/Updated/Deleted owned
//    entities and logs any with a dead owner immediately (log is flushed per line).
//  * Guard (setting, on by default): for objects with a dead owner, removes the dangling
//    Owner component so the vanilla job skips them instead of crashing.
//
// Output: Logs/OwnerCheck.log

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace OwnerCheck
{
    internal static class OCLog
    {
        private static StreamWriter s_Writer;

        public static void Init()
        {
            try
            {
                string logDir = Path.Combine(Application.persistentDataPath, "Logs");
                Directory.CreateDirectory(logDir);
                s_Writer = new StreamWriter(Path.Combine(logDir, "OwnerCheck.log"), false);
                s_Writer.AutoFlush = true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[OwnerCheck] could not open log: " + e);
            }
        }

        public static void Line(string text)
        {
            try
            {
                if (s_Writer != null)
                    s_Writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + text);
            }
            catch { }
        }

        public static void Close()
        {
            try { if (s_Writer != null) s_Writer.Dispose(); } catch { }
            s_Writer = null;
        }
    }

    internal static class Checker
    {
        private const string DeadOwner = "DEAD OWNER";
        private static readonly HashSet<long> s_Reported = new HashSet<long>();

        private static bool GuardEnabled { get { return Mod.Settings == null || Mod.Settings.Guard; } }
        private static bool Verbose { get { return Mod.Settings != null && Mod.Settings.VerboseLog; } }

        public static void ResetSession()
        {
            s_Reported.Clear();
        }

        private static string PrefabName(EntityManager em, PrefabSystem prefabs, Entity e)
        {
            try
            {
                if (em.HasComponent<PrefabRef>(e))
                    return prefabs.GetPrefabName(em.GetComponentData<PrefabRef>(e).m_Prefab);
            }
            catch { }
            return "?";
        }

        private static string Describe(EntityManager em, PrefabSystem prefabs, Entity e)
        {
            string pos = "-";
            if (em.HasComponent<Game.Objects.Transform>(e))
                pos = em.GetComponentData<Game.Objects.Transform>(e).m_Position.ToString();

            string kind =
                em.HasComponent<Game.Objects.Object>(e) ? "Object" :
                em.HasComponent<Game.Net.Edge>(e) ? "NetEdge" :
                em.HasComponent<Game.Net.Node>(e) ? "NetNode" :
                em.HasComponent<Game.Net.Lane>(e) ? "Lane" :
                em.HasComponent<Game.Areas.Area>(e) ? "Area" : "Other";

            string flags = "";
            if (em.HasComponent<Created>(e)) flags += "Created ";
            if (em.HasComponent<Updated>(e)) flags += "Updated ";
            if (em.HasComponent<Deleted>(e)) flags += "Deleted ";
            if (em.HasComponent<Temp>(e)) flags += "Temp ";
            if (em.HasComponent<Game.Buildings.Building>(e)) flags += "Building ";
            if (em.HasComponent<Game.Buildings.Extension>(e)) flags += "Extension ";

            return string.Format("{0} kind={1} prefab='{2}' pos={3} flags=[{4}]", e, kind, PrefabName(em, prefabs, e), pos, flags.Trim());
        }

        /// Checks the entities in the query. Returns number of new problems found.
        public static int Check(EntityManager em, EntityQuery query, PrefabSystem prefabs, string where, bool allowGuard)
        {
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            List<Entity> toDetach = null;
            Dictionary<string, int> prefabCounts = null;
            int deadOwners = 0, deadOriginals = 0, realObjects = 0;
            bool guard = allowGuard && GuardEnabled;
            bool verbose = Verbose;

            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity e = entities[i];
                    Entity owner = em.GetComponentData<Owner>(e).m_Owner;
                    if (owner == Entity.Null)
                        continue;

                    string problem = null;
                    if (!em.Exists(owner))
                    {
                        problem = DeadOwner;
                    }
                    else if (em.HasComponent<Temp>(owner))
                    {
                        Entity original = em.GetComponentData<Temp>(owner).m_Original;
                        if (original != Entity.Null && !em.Exists(original))
                            problem = "OWNER IS TEMP WITH DEAD ORIGINAL " + original;
                    }

                    if (problem == null)
                        continue;

                    long key = ((long)e.Index << 32) ^ (uint)owner.Index;
                    if (!s_Reported.Add(key))
                        continue;

                    if (problem == DeadOwner) deadOwners++; else deadOriginals++;
                    if (!em.HasComponent<Temp>(e)) realObjects++;

                    if (verbose)
                    {
                        OCLog.Line(string.Format("[{0}] frame {1}: {2} -> owner {3}  | {4}",
                            where, Time.frameCount, problem, owner, Describe(em, prefabs, e)));
                    }
                    else
                    {
                        if (prefabCounts == null) prefabCounts = new Dictionary<string, int>();
                        string name = PrefabName(em, prefabs, e);
                        int n;
                        prefabCounts.TryGetValue(name, out n);
                        prefabCounts[name] = n + 1;
                    }

                    if (guard && problem == DeadOwner && em.HasComponent<Game.Objects.Object>(e))
                    {
                        if (toDetach == null) toDetach = new List<Entity>();
                        toDetach.Add(e);
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }

            int detached = 0;
            if (toDetach != null)
            {
                foreach (Entity e in toDetach)
                {
                    try
                    {
                        em.RemoveComponent<Owner>(e);
                        detached++;
                        if (verbose)
                            OCLog.Line(string.Format("[{0}] GUARD: removed dangling Owner from {1}", where, e));
                    }
                    catch (Exception ex)
                    {
                        OCLog.Line("[" + where + "] GUARD failed for " + e + ": " + ex.Message);
                    }
                }
            }

            int found = deadOwners + deadOriginals;
            if (found > 0 && !verbose)
            {
                string prefabsText = prefabCounts == null ? "" : string.Join(", ",
                    prefabCounts.OrderByDescending(p => p.Value).Take(6).Select(p => p.Key + " x" + p.Value).ToArray());
                OCLog.Line(string.Format("[{0}] frame {1}: {2} with dead owner, {3} with dead temp original ({4} non-preview). Guard detached {5}. Prefabs: {6}",
                    where, Time.frameCount, deadOwners, deadOriginals, realObjects, detached, prefabsText));
            }
            return found;
        }
    }

    /// Read-only full scan once after a save has finished loading.
    public partial class OwnerScanSystem : GameSystemBase
    {
        private EntityQuery m_AllOwned;
        private PrefabSystem m_PrefabSystem;
        private bool m_ScanPending;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_AllOwned = GetEntityQuery(ComponentType.ReadOnly<Owner>());
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            Checker.ResetSession();
            m_ScanPending = mode == GameMode.Game;
            OCLog.Line("Loading complete (" + purpose + ", " + mode + ")." + (m_ScanPending ? " Full scan scheduled." : ""));
        }

        protected override void OnUpdate()
        {
            if (!m_ScanPending)
                return;
            m_ScanPending = false;

            int total = m_AllOwned.CalculateEntityCount();
            int found = Checker.Check(EntityManager, m_AllOwned, m_PrefabSystem, "LoadScan", false);
            OCLog.Line(string.Format("Full scan: {0} owned entities checked, {1} with a dead owner (read-only). Watching changes from now on.", total, found));
        }
    }

    /// Per-frame watch of entities being created/updated/deleted.
    public abstract partial class OwnerWatchBase : GameSystemBase
    {
        private EntityQuery m_Changed;
        private PrefabSystem m_PrefabSystem;

        protected abstract string PhaseName { get; }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Changed = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Owner>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() }
            });
        }

        protected override void OnUpdate()
        {
            if (m_Changed.IsEmptyIgnoreFilter)
                return;
            Checker.Check(EntityManager, m_Changed, m_PrefabSystem, PhaseName, true);
        }
    }

    public partial class WatchApplyTool : OwnerWatchBase { protected override string PhaseName { get { return "ApplyTool"; } } }
    public partial class WatchModification1 : OwnerWatchBase { protected override string PhaseName { get { return "Modification1"; } } }
    public partial class WatchModification2 : OwnerWatchBase { protected override string PhaseName { get { return "Modification2"; } } }
    public partial class WatchModification3 : OwnerWatchBase { protected override string PhaseName { get { return "Modification3"; } } }
    public partial class WatchModification4 : OwnerWatchBase { protected override string PhaseName { get { return "Modification4"; } } }
    public partial class WatchModification5 : OwnerWatchBase { protected override string PhaseName { get { return "Modification5"; } } }
    public partial class WatchModificationEnd : OwnerWatchBase { protected override string PhaseName { get { return "ModificationEnd"; } } }
}
