// OwnerCheck - diagnostic local mod for Cities: Skylines II.
// Finds objects whose Owner points to an entity that no longer exists - the exact
// condition that crashes Game.Objects.SubObjectReferencesSystem+UpdateSubObjectReferencesJob.
//
//  * On save load: read-only scan of every owned entity, logs dead owners.
//  * Every frame (in several update phases): checks only Created/Updated/Deleted owned
//    entities and logs any with a dead owner immediately (log is flushed per line).
//  * Guard (on by default): for objects with a dead owner, removes the dangling Owner
//    component so the vanilla job skips them instead of crashing.
//    Disable by creating  ModsData\OwnerCheck\guard_off.txt
//
// Output: Logs\OwnerCheck.log

using System;
using System.Collections.Generic;
using System.IO;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Modding;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace OwnerCheck
{
    public class Mod : IMod
    {
        public void OnLoad(UpdateSystem updateSystem)
        {
            OCLog.Init();
            OCLog.Line("OwnerCheck loaded. Guard " + (Checker.GuardEnabled ? "ON" : "OFF (guard_off.txt present)"));
            updateSystem.UpdateAt<OwnerScanSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAt<WatchApplyTool>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateAt<WatchModification1>(SystemUpdatePhase.Modification1);
            updateSystem.UpdateAt<WatchModification2>(SystemUpdatePhase.Modification2);
            updateSystem.UpdateAt<WatchModification3>(SystemUpdatePhase.Modification3);
            updateSystem.UpdateAt<WatchModification4>(SystemUpdatePhase.Modification4);
            updateSystem.UpdateAt<WatchModification5>(SystemUpdatePhase.Modification5);
            updateSystem.UpdateAt<WatchModificationEnd>(SystemUpdatePhase.ModificationEnd);
        }

        public void OnDispose()
        {
            OCLog.Line("OwnerCheck disposed.");
            OCLog.Close();
        }
    }

    internal static class OCLog
    {
        private static StreamWriter s_Writer;

        public static string DataDir
        {
            get { return Path.Combine(Application.persistentDataPath, "ModsData", "OwnerCheck"); }
        }

        public static void Init()
        {
            try
            {
                string logDir = Path.Combine(Application.persistentDataPath, "Logs");
                Directory.CreateDirectory(logDir);
                Directory.CreateDirectory(DataDir);
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
        private static readonly HashSet<long> s_Reported = new HashSet<long>();
        public static int TotalFound;
        public static int TotalGuarded;

        public static bool GuardEnabled
        {
            get
            {
                try { return !File.Exists(Path.Combine(OCLog.DataDir, "guard_off.txt")); }
                catch { return true; }
            }
        }

        public static void ResetSession()
        {
            s_Reported.Clear();
            TotalFound = 0;
            TotalGuarded = 0;
        }

        public static string Describe(EntityManager em, PrefabSystem prefabs, Entity e)
        {
            string prefab = "?";
            try
            {
                if (em.HasComponent<PrefabRef>(e))
                    prefab = prefabs.GetPrefabName(em.GetComponentData<PrefabRef>(e).m_Prefab);
            }
            catch { }

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

            return string.Format("{0} kind={1} prefab='{2}' pos={3} flags=[{4}]", e, kind, prefab, pos, flags.Trim());
        }

        /// Checks the entities in the query. Returns number of new problems found.
        public static int Check(EntityManager em, EntityQuery query, PrefabSystem prefabs, string where, bool allowGuard)
        {
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            List<Entity> toDetach = null;
            int found = 0;
            try
            {
                bool guard = allowGuard && GuardEnabled;
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity e = entities[i];
                    Entity owner = em.GetComponentData<Owner>(e).m_Owner;
                    if (owner == Entity.Null)
                        continue;

                    string problem = null;
                    if (!em.Exists(owner))
                    {
                        problem = "DEAD OWNER";
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

                    found++;
                    TotalFound++;
                    OCLog.Line(string.Format("[{0}] frame {1}: {2} -> owner {3}  | {4}",
                        where, Time.frameCount, problem, owner, Describe(em, prefabs, e)));

                    if (guard && problem == "DEAD OWNER" && em.HasComponent<Game.Objects.Object>(e))
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

            if (toDetach != null)
            {
                foreach (Entity e in toDetach)
                {
                    try
                    {
                        em.RemoveComponent<Owner>(e);
                        TotalGuarded++;
                        OCLog.Line(string.Format("[{0}] GUARD: removed dangling Owner from {1} (game will treat it as a standalone object)", where, e));
                    }
                    catch (Exception ex)
                    {
                        OCLog.Line("[" + where + "] GUARD failed for " + e + ": " + ex.Message);
                    }
                }
            }
            return found;
        }
    }

    /// Read-only full scan once after a save has finished loading.
    public class OwnerScanSystem : GameSystemBase
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
            OCLog.Line("Loading complete (" + purpose + ", " + mode + "). Full scan scheduled: " + m_ScanPending);
        }

        protected override void OnUpdate()
        {
            if (!m_ScanPending)
                return;
            m_ScanPending = false;

            int total = m_AllOwned.CalculateEntityCount();
            OCLog.Line("Full scan: " + total + " owned entities...");
            int found = Checker.Check(EntityManager, m_AllOwned, m_PrefabSystem, "LoadScan", false);
            OCLog.Line("Full scan done: " + found + " entities with a dead owner (read-only, nothing changed).");
            OCLog.Line("Now watching every frame for dead owners on created/updated/deleted entities.");
        }
    }

    /// Per-frame watch of entities being created/updated/deleted.
    public abstract class OwnerWatchBase : GameSystemBase
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

    public class WatchApplyTool : OwnerWatchBase { protected override string PhaseName { get { return "ApplyTool"; } } }
    public class WatchModification1 : OwnerWatchBase { protected override string PhaseName { get { return "Modification1"; } } }
    public class WatchModification2 : OwnerWatchBase { protected override string PhaseName { get { return "Modification2"; } } }
    public class WatchModification3 : OwnerWatchBase { protected override string PhaseName { get { return "Modification3"; } } }
    public class WatchModification4 : OwnerWatchBase { protected override string PhaseName { get { return "Modification4"; } } }
    public class WatchModification5 : OwnerWatchBase { protected override string PhaseName { get { return "Modification5"; } } }
    public class WatchModificationEnd : OwnerWatchBase { protected override string PhaseName { get { return "ModificationEnd"; } } }
}
