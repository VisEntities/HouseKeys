/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Newtonsoft.Json;
using Rust;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static BuildingManager;

namespace Oxide.Plugins
{
    [Info("House Keys", "VisEntities", "1.0.0")]
    [Description("Enables remote control of doors, locks, and turrets in any building.")]
    public class HouseKeys : RustPlugin
    {
        #region Fields

        private static HouseKeys _plugin;
        private static Configuration _config;
        private const int LAYER_BUILDING = Layers.Mask.Construction;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Building Detection Range")]
            public float BuildingDetectionRange { get; set; }

            [JsonProperty("Enable Visualization")]
            public bool EnableVisualization { get; set; }

            [JsonProperty("Visualization Duration Seconds")]
            public float VisualizationDurationSeconds { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                BuildingDetectionRange = 25,
                EnableVisualization = true,
                VisualizationDurationSeconds = 10f
            };
        }

        #endregion Configuration

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            PermissionUtil.RegisterPermissions();
        }

        private void Unload()
        {
            CoroutineUtil.StopAllCoroutines();
            _config = null;
            _plugin = null;
        }

        #endregion Oxide Hooks

        #region Building Retrieval

        private BuildingBlock FindBuildingBlockInSight(BasePlayer player, float distance)
        {
            RaycastHit raycastHit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out raycastHit, distance, LAYER_BUILDING, QueryTriggerInteraction.Ignore))
                return null;

            BuildingBlock buildingBlock = raycastHit.GetEntity() as BuildingBlock;
            if (buildingBlock == null)
                return null;

            return buildingBlock;
        }

        private Building TryGetBuildingForEntity(BaseEntity entity, int minimumBuildingBlocks, bool mustHaveBuildingPrivilege = true)
        {
            BuildingBlock buildingBlock = entity as BuildingBlock;
            DecayEntity decayEntity = entity as DecayEntity;

            uint buildingId = 0;
            if (buildingBlock != null)
            {
                buildingId = buildingBlock.buildingID;
            }
            else if (decayEntity != null)
            {
                buildingId = decayEntity.buildingID;
            }

            Building building = server.GetBuilding(buildingId);
            if (building != null &&
                building.buildingBlocks.Count >= minimumBuildingBlocks &&
                (!mustHaveBuildingPrivilege || building.HasBuildingPrivileges()))
            {
                return building;
            }

            return null;
        }

        #endregion Building Retrieval

        #region Permissions

        private static class PermissionUtil
        {
            public const string USE = "housekeys.use";
            private static readonly List<string> _permissions = new List<string>
            {
                USE,
            };

            public static void RegisterPermissions()
            {
                foreach (var permission in _permissions)
                {
                    _plugin.permission.RegisterPermission(permission, _plugin);
                }
            }

            public static bool HasPermission(BasePlayer player, string permissionName)
            {
                return _plugin.permission.UserHasPermission(player.UserIDString, permissionName);
            }
        }

        #endregion Permissions

        #region Helper Classes

        public static class DrawUtil
        {
            public static void Box(BasePlayer player, float durationSeconds, Color color, Vector3 position, float radius)
            {
                player.SendConsoleCommand("ddraw.box", durationSeconds, color, position, radius);
            }

            public static void Sphere(BasePlayer player, float durationSeconds, Color color, Vector3 position, float radius)
            {
                player.SendConsoleCommand("ddraw.sphere", durationSeconds, color, position, radius);
            }

            public static void Line(BasePlayer player, float durationSeconds, Color color, Vector3 fromPosition, Vector3 toPosition)
            {
                player.SendConsoleCommand("ddraw.line", durationSeconds, color, fromPosition, toPosition);
            }

            public static void Arrow(BasePlayer player, float durationSeconds, Color color, Vector3 fromPosition, Vector3 toPosition, float headSize)
            {
                player.SendConsoleCommand("ddraw.arrow", durationSeconds, color, fromPosition, toPosition, headSize);
            }

            public static void Text(BasePlayer player, float durationSeconds, Color color, Vector3 position, string text)
            {
                player.SendConsoleCommand("ddraw.text", durationSeconds, color, position, text);
            }
        }

        public static class CoroutineUtil
        {
            private static readonly Dictionary<string, Coroutine> _activeCoroutines = new Dictionary<string, Coroutine>();

            public static void StartCoroutine(string coroutineName, IEnumerator coroutineFunction)
            {
                StopCoroutine(coroutineName);

                Coroutine coroutine = ServerMgr.Instance.StartCoroutine(coroutineFunction);
                _activeCoroutines[coroutineName] = coroutine;
            }

            public static void StopCoroutine(string coroutineName)
            {
                if (_activeCoroutines.TryGetValue(coroutineName, out Coroutine coroutine))
                {
                    if (coroutine != null)
                        ServerMgr.Instance.StopCoroutine(coroutine);

                    _activeCoroutines.Remove(coroutineName);
                }
            }

            public static void StopAllCoroutines()
            {
                foreach (string coroutineName in _activeCoroutines.Keys.ToArray())
                {
                    StopCoroutine(coroutineName);
                }
            }
        }

        #endregion Helper Classes

        #region House Management

        #region Doors

        private IEnumerator OpenOrCloseDoors(BasePlayer player, Building building, bool open, Action<int> onComplete)
        {
            int count = 0;
            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is Door door)
                {
                    door.SetOpen(open);
                    count++;

                    if (_config.EnableVisualization)
                    {
                        DrawUtil.Box(player, _config.VisualizationDurationSeconds, Color.black, door.WorldSpaceBounds().position, 0.5f);
                        DrawUtil.Text(player, _config.VisualizationDurationSeconds, Color.white, door.WorldSpaceBounds().position, door.ShortPrefabName);
                    }
                }

                yield return null;
            }

            if (onComplete != null)
                onComplete.Invoke(count);
        }

        #endregion Doors

        #region Locks

        private IEnumerator LockOrUnlockCodeLocks(BasePlayer player, Building building, bool locked, Action<int> onComplete)
        {
            int count = 0;
            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is BaseEntity baseEntity)
                {
                    BaseLock baseLock = baseEntity.GetSlot(BaseEntity.Slot.Lock) as BaseLock;
                    if (baseLock != null)
                    {
                        baseLock.SetFlag(BaseEntity.Flags.Locked, locked);
                        count++;

                        if (_config.EnableVisualization)
                        {
                            DrawUtil.Box(player, _config.VisualizationDurationSeconds, Color.black, baseLock.WorldSpaceBounds().position, 0.5f);
                            DrawUtil.Text(player, _config.VisualizationDurationSeconds, Color.white, baseLock.WorldSpaceBounds().position, baseLock.ShortPrefabName);
                        }
                    }
                }

                yield return null;
            }

            if (onComplete != null)
                onComplete.Invoke(count);
        }

        private IEnumerator ClearAuthCodeLocks(BasePlayer player, Building building, Action<int> onComplete)
        {
            int count = 0;
            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is BaseEntity baseEntity)
                {
                    CodeLock codeLock = baseEntity.GetSlot(BaseEntity.Slot.Lock) as CodeLock;
                    if (codeLock != null)
                    {
                        codeLock.whitelistPlayers.Clear();
                        codeLock.guestPlayers.Clear();
                        count++;

                        if (_config.EnableVisualization)
                        {
                            DrawUtil.Box(player, _config.VisualizationDurationSeconds, Color.black, codeLock.WorldSpaceBounds().position, 0.5f);
                            DrawUtil.Text(player, _config.VisualizationDurationSeconds, Color.white, codeLock.WorldSpaceBounds().position, codeLock.ShortPrefabName);
                        }
                    }
                }

                yield return null;
            }

            if (onComplete != null)
                onComplete.Invoke(count);
        }

        private IEnumerator ChangeCodeLocks(BasePlayer player, Building building, string newCode, Action<int> onComplete)
        {
            int count = 0;
            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is BaseEntity baseEntity)
                {
                    CodeLock codeLock = baseEntity.GetSlot(BaseEntity.Slot.Lock) as CodeLock;
                    if (codeLock != null)
                    {
                        codeLock.code = newCode;
                        codeLock.SetFlag(BaseEntity.Flags.Locked, true);
                        count++;

                        if (_config.EnableVisualization)
                        {
                            DrawUtil.Box(player, _config.VisualizationDurationSeconds, Color.black, codeLock.WorldSpaceBounds().position, 0.5f);
                            DrawUtil.Text(player, _config.VisualizationDurationSeconds, Color.white, codeLock.WorldSpaceBounds().position, codeLock.ShortPrefabName);
                        }
                    }
                }

                yield return null;
            }

            if (onComplete != null)
                onComplete.Invoke(count);
        }

        #endregion Locks

        #region Traps

        private IEnumerator TurnOffOrOnAutoTurrets(BasePlayer player, Building building, bool turnOn, Action<int> onComplete)
        {
            int count = 0;
            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is AutoTurret autoTurret)
                {
                    if (turnOn)
                        autoTurret.InitiateStartup();
                    else
                        autoTurret.InitiateShutdown();
                    count++;

                    if (_config.EnableVisualization)
                    {
                        DrawUtil.Box(player, _config.VisualizationDurationSeconds, Color.black, autoTurret.WorldSpaceBounds().position, 0.5f);
                        DrawUtil.Text(player, _config.VisualizationDurationSeconds, Color.white, autoTurret.WorldSpaceBounds().position, autoTurret.ShortPrefabName);
                    }
                }

                yield return null;
            }

            if (onComplete != null)
                onComplete.Invoke(count);
        }

        private IEnumerator UnloadAutoTurrets(BasePlayer player, Building building, Action<int> onComplete)
        {
            int count = 0;
            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is AutoTurret autoTurret)
                {
                    if (UnloadTrapFromAmmo(decayEntity as StorageContainer))
                    {
                        count++;
                        if (_config.EnableVisualization)
                        {
                            DrawUtil.Box(player, _config.VisualizationDurationSeconds, Color.black, autoTurret.WorldSpaceBounds().position, 0.5f);
                            DrawUtil.Text(player, _config.VisualizationDurationSeconds, Color.white, autoTurret.WorldSpaceBounds().position, autoTurret.ShortPrefabName);
                        }
                    }
                }

                yield return null;
            }

            if (onComplete != null)
                onComplete.Invoke(count);
        }

        private IEnumerator UnloadTraps(BasePlayer player, Building building, Action<int> onComplete)
        {
            int count = 0;
            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is GunTrap || decayEntity is FlameTurret)
                {
                    if (UnloadTrapFromAmmo(decayEntity as StorageContainer))
                    {
                        count++;
                        if (_config.EnableVisualization)
                        {
                            DrawUtil.Box(player, _config.VisualizationDurationSeconds, Color.black, decayEntity.WorldSpaceBounds().position, 0.5f);
                            DrawUtil.Text(player, _config.VisualizationDurationSeconds, Color.white, decayEntity.WorldSpaceBounds().position, decayEntity.ShortPrefabName);
                        }
                    }
                }

                yield return null;
            }

            if (onComplete != null)
                onComplete.Invoke(count);
        }

        private IEnumerator ClearAuthAutoTurrets(BasePlayer player, Building building, Action<int> onComplete)
        {
            int count = 0;
            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is AutoTurret autoTurret)
                {
                    autoTurret.authorizedPlayers.Clear();
                    count++;

                    if (_config.EnableVisualization)
                    {
                        DrawUtil.Box(player, _config.VisualizationDurationSeconds, Color.black, autoTurret.WorldSpaceBounds().position, 0.5f);
                        DrawUtil.Text(player, _config.VisualizationDurationSeconds, Color.white, autoTurret.WorldSpaceBounds().position, autoTurret.ShortPrefabName);
                    }
                }

                yield return null;
            }

            if (onComplete != null)
                onComplete.Invoke(count);
        }

        private bool UnloadTrapFromAmmo(StorageContainer ammoContainer)
        {
            if (ammoContainer == null || ammoContainer.inventory == null)
                return false;

            bool unloaded = false;
            foreach (Item item in ammoContainer.inventory.itemList)
            {
                if (item != null && item.amount > 0)
                {
                    item.Remove();
                    unloaded = true;
                }
            }
            return unloaded;
        }

        #endregion Traps

        #region Tool Cupboards

        private IEnumerator ClearAuthCupboards(BasePlayer player, Building building, Action<int> onComplete)
        {
            int count = 0;
            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is BuildingPrivlidge cupboard)
                {
                    cupboard.authorizedPlayers.Clear();
                    count++;

                    if (_config.EnableVisualization)
                    {
                        DrawUtil.Box(player, _config.VisualizationDurationSeconds, Color.black, cupboard.WorldSpaceBounds().position, 0.5f);
                        DrawUtil.Text(player, _config.VisualizationDurationSeconds, Color.white, cupboard.WorldSpaceBounds().position, cupboard.ShortPrefabName);
                    }
                }

                yield return null;
            }

            if (onComplete != null)
                onComplete.Invoke(count);
        }

        #endregion Tool Cupboards

        #endregion House Management

        #region Commands

        private static class Cmd
        {
            /// <summary>
            /// house.door open
            /// house.door close
            /// </summary>
            public const string DOOR = "house.door";

            /// <summary>
            /// house.lock unlock
            /// house.lock lock
            /// house.lock auth clear
            /// house.lock code <newcode>
            /// </summary>
            public const string LOCK = "house.lock";

            /// <summary>
            /// house.turret on
            /// house.turret off
            /// house.turret unload
            /// house.turret auth clear
            /// </summary>
            public const string TURRET = "house.turret";

            /// <summary>
            /// house.trap unload
            /// </summary>
            public const string TRAP = "house.trap";

            /// <summary>
            /// house.cupboard auth clear
            /// </summary>
            public const string CUPBOARD = "house.cupboard";
        }

        [ChatCommand(Cmd.DOOR)]
        private void cmdDoor(BasePlayer player, string cmd, string[] args)
        {
            if (player == null)
                return;

            if (!PermissionUtil.HasPermission(player, PermissionUtil.USE))
            {
                SendMessage(player, Lang.NoPermission);
                return;
            }

            if (args.Length != 1 || (args[0] != "open" && args[0] != "close"))
            {
                SendMessage(player, Lang.InvalidArgs,
                    $"  -<color=#F0E68C> {Cmd.DOOR} open</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.DOOR} close</color>");
                return;
            }

            bool open = args[0] == "open";

            BuildingBlock buildingBlock = FindBuildingBlockInSight(player, _config.BuildingDetectionRange);
            if (buildingBlock == null)
            {
                SendMessage(player, Lang.NoBuildingFound);
                return;
            }

            Building building = TryGetBuildingForEntity(buildingBlock, minimumBuildingBlocks: 1, mustHaveBuildingPrivilege: false);
            if (building == null)
            {
                SendMessage(player, Lang.NoBuildingFound);
                return;
            }

            CoroutineUtil.StartCoroutine("OpenOrCloseDoors", OpenOrCloseDoors(player, building, open, count =>
            {
                if (open)
                {
                    if (count > 0)
                        SendMessage(player, Lang.DoorsOpened, count);
                    else
                        SendMessage(player, Lang.NoDoorsToOpen);
                }
                else
                {
                    if (count > 0)
                        SendMessage(player, Lang.DoorsClosed, count);
                    else
                        SendMessage(player, Lang.NoDoorsToClose);
                }
            }));
        }

        [ChatCommand(Cmd.LOCK)]
        private void cmdLock(BasePlayer player, string cmd, string[] args)
        {
            if (player == null)
                return;

            if (!PermissionUtil.HasPermission(player, PermissionUtil.USE))
            {
                SendMessage(player, Lang.NoPermission);
                return;
            }

            if (args.Length < 1 || (args[0] != "unlock" && args[0] != "lock" && args[0] != "auth" && args[0] != "code"))
            {
                SendMessage(player, Lang.InvalidArgs,
                    $"  -<color=#F0E68C> {Cmd.LOCK} unlock</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.LOCK} lock</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.LOCK} auth clear</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.LOCK} code <newcode></color>");
                return;
            }

            BuildingBlock buildingBlock = FindBuildingBlockInSight(player, _config.BuildingDetectionRange);
            if (buildingBlock == null)
            {
                SendMessage(player, Lang.NoBuildingFound);
                return;
            }

            Building building = TryGetBuildingForEntity(buildingBlock, minimumBuildingBlocks: 1, mustHaveBuildingPrivilege: false);
            if (building == null)
            {
                SendMessage(player, Lang.NoBuildingFound);
                return;
            }

            if (args[0] == "unlock")
            {
                CoroutineUtil.StartCoroutine("LockOrUnlockCodeLocks", LockOrUnlockCodeLocks(player, building, false, count =>
                {
                    if (count > 0)
                    {
                        SendMessage(player, Lang.LocksUnlocked, count);
                    }
                    else
                    {
                        SendMessage(player, Lang.NoLocksToUnlock);
                    }
                }));
            }
            else if (args[0] == "lock")
            {
                CoroutineUtil.StartCoroutine("LockOrUnlockCodeLocks", LockOrUnlockCodeLocks(player, building, true, count =>
                {
                    if (count > 0)
                    {
                        SendMessage(player, Lang.LocksLocked, count);
                    }
                    else
                    {
                        SendMessage(player, Lang.NoLocksToLock);
                    }
                }));
            }
            else if (args[0] == "auth" && args.Length == 2 && args[1] == "clear")
            {
                CoroutineUtil.StartCoroutine("ClearAuthCodeLocks", ClearAuthCodeLocks(player, building, count =>
                {
                    if (count > 0)
                    {
                        SendMessage(player, Lang.AuthCleared, count);
                    }
                    else
                    {
                        SendMessage(player, Lang.NoAuthToClear);
                    }
                }));
            }
            else if (args[0] == "code" && args.Length == 2)
            {
                string newCode = args[1];
                CoroutineUtil.StartCoroutine("ChangeCodeLocks", ChangeCodeLocks(player, building, newCode, count =>
                {
                    if (count > 0)
                    {
                        SendMessage(player, Lang.CodeChanged, count, newCode);
                    }
                    else
                    {
                        SendMessage(player, Lang.NoCodesToChange);
                    }
                }));
            }
            else
            {
                SendMessage(player, Lang.InvalidArgs,
                    $"  -<color=#F0E68C> {Cmd.LOCK} unlock</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.LOCK} lock</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.LOCK} auth clear</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.LOCK} code <newcode></color>");
            }
        }

        [ChatCommand(Cmd.TURRET)]
        private void cmdTurret(BasePlayer player, string cmd, string[] args)
        {
            if (player == null)
                return;

            if (!PermissionUtil.HasPermission(player, PermissionUtil.USE))
            {
                SendMessage(player, Lang.NoPermission);
                return;
            }

            if (args.Length < 1 || (args[0] != "on" && args[0] != "off" && args[0] != "unload" && args[0] != "auth"))
            {
                SendMessage(player, Lang.InvalidArgs,
                    $"  -<color=#F0E68C> {Cmd.TURRET} on</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} off</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} unload</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} auth clear</color>");
                return;
            }

            BuildingBlock buildingBlock = FindBuildingBlockInSight(player, _config.BuildingDetectionRange);
            if (buildingBlock == null)
            {
                SendMessage(player, Lang.NoBuildingFound);
                return;
            }

            Building building = TryGetBuildingForEntity(buildingBlock, minimumBuildingBlocks: 1, mustHaveBuildingPrivilege: false);
            if (building == null)
            {
                SendMessage(player, Lang.NoBuildingFound);
                return;
            }

            if (args[0] == "on" || args[0] == "off")
            {
                bool turnOn = args[0] == "on";
                CoroutineUtil.StartCoroutine("TurnOffOrOnAutoTurrets", TurnOffOrOnAutoTurrets(player, building, turnOn, count =>
                {
                    if (turnOn)
                    {
                        if (count > 0)
                            SendMessage(player, Lang.TurretsOn, count);
                        else
                            SendMessage(player, Lang.NoTurretsToTurnOn);
                    }
                    else
                    {
                        if (count > 0)
                            SendMessage(player, Lang.TurretsOff, count);
                        else
                            SendMessage(player, Lang.NoTurretsToTurnOff);
                    }
                }));
            }
            else if (args[0] == "unload")
            {
                CoroutineUtil.StartCoroutine("UnloadAutoTurrets", UnloadAutoTurrets(player, building, count =>
                {
                    if (count > 0)
                    {
                        SendMessage(player, Lang.TurretsUnloaded, count);
                    }
                    else
                    {
                        SendMessage(player, Lang.NoTurretsToUnload);
                    }
                }));
            }
            else if (args[0] == "auth" && args.Length == 2 && args[1] == "clear")
            {
                CoroutineUtil.StartCoroutine("ClearAuthAutoTurrets", ClearAuthAutoTurrets(player, building, count =>
                {
                    if (count > 0)
                    {
                        SendMessage(player, Lang.TurretAuthCleared, count);
                    }
                    else
                    {
                        SendMessage(player, Lang.NoTurretAuthToClear);
                    }
                }));
            }
            else
            {
                SendMessage(player, Lang.InvalidArgs,
                    $"  -<color=#F0E68C> {Cmd.TURRET} on</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} off</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} unload</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} auth clear</color>");
            }
        }

        [ChatCommand(Cmd.CUPBOARD)]
        private void cmdCupboard(BasePlayer player, string cmd, string[] args)
        {
            if (player == null)
                return;

            if (!PermissionUtil.HasPermission(player, PermissionUtil.USE))
            {
                SendMessage(player, Lang.NoPermission);
                return;
            }

            if (args.Length != 1 || args[0] != "auth" || args[0] != "clear")
            {
                SendMessage(player, Lang.InvalidArgs,
                    $"  -<color=#F0E68C> {Cmd.CUPBOARD} auth clear</color>");
                return;
            }

            BuildingBlock buildingBlock = FindBuildingBlockInSight(player, _config.BuildingDetectionRange);
            if (buildingBlock == null)
            {
                SendMessage(player, Lang.NoBuildingFound);
                return;
            }

            Building building = TryGetBuildingForEntity(buildingBlock, minimumBuildingBlocks: 1, mustHaveBuildingPrivilege: false);
            if (building == null)
            {
                SendMessage(player, Lang.NoBuildingFound);
                return;
            }

            CoroutineUtil.StartCoroutine("ClearAuthCupboards", ClearAuthCupboards(player, building, count =>
            {
                if (count > 0)
                {
                    SendMessage(player, Lang.CupboardAuthCleared, count);
                }
                else
                {
                    SendMessage(player, Lang.NoCupboardAuthToClear);
                }
            }));
        }

        [ChatCommand(Cmd.TRAP)]
        private void cmdTrap(BasePlayer player, string cmd, string[] args)
        {
            if (player == null)
                return;

            if (!PermissionUtil.HasPermission(player, PermissionUtil.USE))
            {
                SendMessage(player, Lang.NoPermission);
                return;
            }

            if (args.Length != 1 || args[0] != "unload")
            {
                SendMessage(player, Lang.InvalidArgs,
                    $"  -<color=#F0E68C> {Cmd.TRAP} unload</color>");
                return;
            }

            BuildingBlock buildingBlock = FindBuildingBlockInSight(player, _config.BuildingDetectionRange);
            if (buildingBlock == null)
            {
                SendMessage(player, Lang.NoBuildingFound);
                return;
            }

            Building building = TryGetBuildingForEntity(buildingBlock, minimumBuildingBlocks: 1, mustHaveBuildingPrivilege: false);
            if (building == null)
            {
                SendMessage(player, Lang.NoBuildingFound);
                return;
            }

            CoroutineUtil.StartCoroutine("UnloadTraps", UnloadTraps(player, building, count =>
            {
                if (count > 0)
                {
                    SendMessage(player, Lang.TrapsUnloaded, count);
                }
                else
                {
                    SendMessage(player, Lang.NoTrapsToUnload);
                }
            }));
        }

        #endregion Commands

        #region Localization

        private class Lang
        {
            public const string NoPermission = "NoPermission";
            public const string InvalidArgs = "InvalidArgs";
            public const string NoBuildingFound = "NoBuildingFound";
            public const string DoorsOpened = "DoorsOpened";
            public const string DoorsClosed = "DoorsClosed";
            public const string NoDoorsToOpen = "NoDoorsToOpen";
            public const string NoDoorsToClose = "NoDoorsToClose";
            public const string LocksLocked = "LocksLocked";
            public const string LocksUnlocked = "LocksUnlocked";
            public const string NoLocksToUnlock = "NoLocksToUnlock";
            public const string NoLocksToLock = "NoLocksToLock";
            public const string AuthCleared = "AuthCleared";
            public const string NoAuthToClear = "NoAuthToClear";
            public const string CodeChanged = "CodeChanged";
            public const string NoCodesToChange = "NoCodesToChange";
            public const string TurretsOn = "TurretsOn";
            public const string TurretsOff = "TurretsOff";
            public const string NoTurretsToTurnOn = "NoTurretsToTurnOn";
            public const string NoTurretsToTurnOff = "NoTurretsToTurnOff";
            public const string TurretsUnloaded = "TurretsUnloaded";
            public const string NoTurretsToUnload = "NoTurretsToUnload";
            public const string TurretAuthCleared = "TurretAuthCleared";
            public const string NoTurretAuthToClear = "NoTurretAuthToClear";
            public const string CupboardAuthCleared = "CupboardAuthCleared";
            public const string NoCupboardAuthToClear = "NoCupboardAuthToClear";
            public const string TrapsUnloaded = "TrapsUnloaded";
            public const string NoTrapsToUnload = "NoTrapsToUnload";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.NoPermission] = "You do not have permission to use this command.",
                [Lang.InvalidArgs] = "Invalid arguments. Usage:\n{0}",
                [Lang.NoBuildingFound] = "No building found in sight.",
                [Lang.DoorsOpened] = "<color=#ADFF2F>{0}</color> doors have been opened.",
                [Lang.DoorsClosed] = "<color=#ADFF2F>{0}</color> doors have been closed.",
                [Lang.NoDoorsToOpen] = "No doors to open.",
                [Lang.NoDoorsToClose] = "No doors to close.",
                [Lang.LocksLocked] = "<color=#ADFF2F>{0}</color> locks have been locked.",
                [Lang.LocksUnlocked] = "<color=#ADFF2F>{0}</color> locks have been unlocked.",
                [Lang.NoLocksToUnlock] = "No locks to unlock.",
                [Lang.NoLocksToLock] = "No locks to lock.",
                [Lang.AuthCleared] = "Authorization cleared for <color=#ADFF2F>{0}</color> code locks.",
                [Lang.NoAuthToClear] = "No code lock authorizations to clear.",
                [Lang.CodeChanged] = "Code changed for <color=#ADFF2F>{0}</color> code locks to <color=#ADFF2F>{1}</color>.",
                [Lang.NoCodesToChange] = "No code locks to change.",
                [Lang.TurretsOn] = "<color=#ADFF2F>{0}</color> auto turrets have been turned on.",
                [Lang.TurretsOff] = "<color=#ADFF2F>{0}</color> auto turrets have been turned off.",
                [Lang.NoTurretsToTurnOn] = "No auto turrets to turn on.",
                [Lang.NoTurretsToTurnOff] = "No auto turrets to turn off.",
                [Lang.TurretsUnloaded] = "<color=#ADFF2F>{0}</color> auto turrets have been unloaded.",
                [Lang.NoTurretsToUnload] = "No auto turrets to unload.",
                [Lang.TurretAuthCleared] = "Authorization cleared for <color=#ADFF2F>{0}</color> auto turrets.",
                [Lang.NoTurretAuthToClear] = "No auto turret authorizations to clear.",
                [Lang.CupboardAuthCleared] = "Authorization cleared for <color=#ADFF2F>{0}</color> cupboards.",
                [Lang.NoCupboardAuthToClear] = "No cupboard authorizations to clear.",
                [Lang.TrapsUnloaded] = "<color=#ADFF2F>{0}</color> traps have been unloaded.",
                [Lang.NoTrapsToUnload] = "No traps to unload.",
            }, this, "en");
        }

        private void SendMessage(BasePlayer player, string messageKey, params object[] args)
        {
            string message = lang.GetMessage(messageKey, this, player.UserIDString);
            if (args.Length > 0)
                message = string.Format(message, args);

            SendReply(player, message);
        }

        #endregion Localization
    }
}