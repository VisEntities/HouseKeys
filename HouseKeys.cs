/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Facepunch;
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
    [Info("House Keys", "VisEntities", "1.2.0")]
    [Description("Enables remote control of doors, locks, and turrets in any building.")]
    public class HouseKeys : RustPlugin
    {
        #region Fields

        private static HouseKeys _plugin;
        private static Configuration _config;
        private const int LAYER_BUILDING = Layers.Mask.Deployed | Layers.Mask.Construction;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Building Detection Range")]
            public float BuildingDetectionRange { get; set; }

            [JsonProperty("Building Has To Have Tool Cupboard")]
            public bool BuildingHasToHaveToolCupboard { get; set; }

            [JsonProperty("Player Has To Have Building Privilege")]
            public bool PlayerHasToHaveBuildingPrivilege { get; set; }

            [JsonProperty("Player Has To Be Owner Of House Entities")]
            public bool PlayerHasToBeOwnerOfHouseEntities { get; set; }

            [JsonProperty("Can Teammates Also Control House Entities")]
            public bool CanTeammatesAlsoControlHouseEntities { get; set; }

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

            if (string.Compare(_config.Version, "1.1.0") < 0)
            {
                _config.BuildingHasToHaveToolCupboard = defaultConfig.BuildingHasToHaveToolCupboard;
                _config.PlayerHasToHaveBuildingPrivilege = defaultConfig.PlayerHasToHaveBuildingPrivilege;
            }

            if (string.Compare(_config.Version, "1.2.0") < 0)
            {
                _config.PlayerHasToBeOwnerOfHouseEntities = defaultConfig.PlayerHasToBeOwnerOfHouseEntities;
                _config.CanTeammatesAlsoControlHouseEntities = defaultConfig.CanTeammatesAlsoControlHouseEntities;
            }

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                BuildingDetectionRange = 25,
                BuildingHasToHaveToolCupboard = true,
                PlayerHasToHaveBuildingPrivilege = true,
                PlayerHasToBeOwnerOfHouseEntities = true,
                CanTeammatesAlsoControlHouseEntities = true,
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

        private BaseEntity GetEntityInSight(BasePlayer player, float distance)
        {
            RaycastHit raycastHit;
            if (Physics.Raycast(player.eyes.HeadRay(), out raycastHit, distance, LAYER_BUILDING, QueryTriggerInteraction.Ignore))
            {
                BaseEntity entity = raycastHit.GetEntity();
                if (entity != null)
                {
                    if (_config.EnableVisualization)
                        VisualizeDetectedEntityInSight(player, player.eyes.position, raycastHit.point, entity.ShortPrefabName);

                    return entity;
                }
            }

            return null;
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
            public const string ALL = "housekeys.all";
            public const string TURRET = "housekeys.turret";
            public const string CUPBOARD = "housekeys.cupboard";
            public const string DOOR = "housekeys.door";
            public const string LOCK = "housekeys.lock";
            public const string TRAP = "housekeys.trap";
            public const string BYPASS = "housekeys.bypass";
            private static readonly List<string> _permissions = new List<string>
            {
                ALL,
                TURRET,
                CUPBOARD,
                DOOR,
                LOCK,
                TRAP,
                BYPASS,
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

        #region Helper Functions

        public static bool OwnerOrTeammate(BasePlayer player, BaseEntity entity)
        {
            if (entity.OwnerID == player.userID)
                return true;

            if (_config.CanTeammatesAlsoControlHouseEntities && AreTeammates(entity.OwnerID, player.userID))
                return true;

            return false;
        }

        public static bool AreTeammates(ulong firstPlayerId, ulong secondPlayerId)
        {
            RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance.FindPlayersTeam(firstPlayerId);
            if (team != null && team.members.Contains(secondPlayerId))
                return true;

            return false;
        }

        public static bool PlayerHasBuildingPrivilege(BasePlayer player, Building building)
        {
            foreach (var privilege in building.buildingPrivileges)
            {
                if (privilege.IsAuthed(player))
                    return true;
            }

            return false;
        }

        #endregion Helper Functions

        #region Visualization

        private void VisualizeDetectedEntityInSight(BasePlayer player, Vector3 startPosition, Vector3 hitPosition, string text)
        {
            bool wasAdmin = player.IsAdmin;
            try
            {
                if (!wasAdmin)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                    player.SendNetworkUpdateImmediate();
                }

                DrawUtil.Arrow(player, _config.VisualizationDurationSeconds, Color.black, startPosition, hitPosition, 0.5f);
                DrawUtil.Text(player, _config.VisualizationDurationSeconds, Color.white, hitPosition, $"<size=30>{text}</size>");
                DrawUtil.Sphere(player, _config.VisualizationDurationSeconds, Color.green, hitPosition, 0.3f);
            }
            finally
            {
                if (!wasAdmin)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                    player.SendNetworkUpdateImmediate();
                }
            }
        }

        private void VisualizeHouseEntity(BasePlayer player, Vector3 entityPosition, string text)
        {
            bool wasAdmin = player.IsAdmin;
            try
            {
                if (!wasAdmin)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                    player.SendNetworkUpdateImmediate();
                }

                DrawUtil.Text(player, _config.VisualizationDurationSeconds, Color.white, entityPosition, $"<size=30>{text}</size>");
                DrawUtil.Box(player, _config.VisualizationDurationSeconds, Color.black, entityPosition, 0.5f);
            }
            finally
            {
                if (!wasAdmin)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                    player.SendNetworkUpdateImmediate();
                }
            }
        }

        #endregion Visualization

        #region House Entities Management

        #region Doors

        private IEnumerator OpenOrCloseDoorsCoroutine(BasePlayer player, Building building, bool open, Action<int, int> onComplete)
        {
            int successCount = 0;
            int failCount = 0;

            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is Door door)
                {
                    if (_config.PlayerHasToBeOwnerOfHouseEntities && !OwnerOrTeammate(player, door))
                    {
                        failCount++;
                        continue;
                    }

                    door.SetOpen(open);
                    successCount++;

                    if (_config.EnableVisualization)
                    {
                        string action = "Closed";
                        if (open)
                        {
                            action = "Opened";
                        }

                        VisualizeHouseEntity(player, door.WorldSpaceBounds().position, $"{door.ShortPrefabName}\n{action}");
                    }
                }

                yield return null;
            }

            if (onComplete != null)
            {
                onComplete.Invoke(successCount, failCount);
            }
        }

        #endregion Doors

        #region Locks

        private IEnumerator LockOrUnlockLocksCoroutine(BasePlayer player, Building building, bool locked, Action<int, int> onComplete)
        {
            int successCount = 0;
            int failCount = 0;

            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is BaseEntity baseEntity)
                {
                    BaseLock baseLock = baseEntity.GetSlot(BaseEntity.Slot.Lock) as BaseLock;
                    if (baseLock != null)
                    {
                        if (_config.PlayerHasToBeOwnerOfHouseEntities && !OwnerOrTeammate(player, baseLock))
                        {
                            failCount++;
                            continue;
                        }

                        baseLock.SetFlag(BaseEntity.Flags.Locked, locked);
                        successCount++;

                        if (_config.EnableVisualization)
                        {
                            string action = "Unlocked";
                            if (locked)
                            {
                                action = "Locked";
                            }

                            VisualizeHouseEntity(player, baseLock.WorldSpaceBounds().position, $"{baseLock.ShortPrefabName}\n{action}");
                        }
                    }
                }

                yield return null;
            }

            if (onComplete != null)
            {
                onComplete.Invoke(successCount, failCount);
            }
        }

        private IEnumerator ClearCodeLockAuthsCoroutine(BasePlayer player, Building building, Action<int, int> onComplete)
        {
            int successCount = 0;
            int failCount = 0;

            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is BaseEntity baseEntity)
                {
                    CodeLock codeLock = baseEntity.GetSlot(BaseEntity.Slot.Lock) as CodeLock;
                    if (codeLock != null)
                    {
                        if (_config.PlayerHasToBeOwnerOfHouseEntities && !OwnerOrTeammate(player, codeLock))
                        {
                            failCount++;
                            continue;
                        }

                        codeLock.whitelistPlayers.Clear();
                        codeLock.guestPlayers.Clear();
                        successCount++;

                        if (_config.EnableVisualization)
                        {
                            VisualizeHouseEntity(player, codeLock.WorldSpaceBounds().position, $"{codeLock.ShortPrefabName}\nAuthorization Cleared");
                        }
                    }
                }

                yield return null;
            }

            if (onComplete != null)
            {
                onComplete.Invoke(successCount, failCount);
            }
        }

        private IEnumerator ChangeCodeForCodeLocksCoroutine(BasePlayer player, Building building, string newCode, Action<int, int> onComplete)
        {
            int successCount = 0;
            int failCount = 0;

            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is BaseEntity baseEntity)
                {
                    CodeLock codeLock = baseEntity.GetSlot(BaseEntity.Slot.Lock) as CodeLock;
                    if (codeLock != null)
                    {
                        if (_config.PlayerHasToBeOwnerOfHouseEntities && !OwnerOrTeammate(player, codeLock))
                        {
                            failCount++;
                            continue;
                        }

                        codeLock.code = newCode;
                        codeLock.SetFlag(BaseEntity.Flags.Locked, true);
                        successCount++;

                        if (_config.EnableVisualization)
                        {
                            VisualizeHouseEntity(player, codeLock.WorldSpaceBounds().position, $"{codeLock.ShortPrefabName}\nCode Changed {newCode}");
                        }
                    }
                }

                yield return null;
            }

            if (onComplete != null)
            {
                onComplete.Invoke(successCount, failCount);
            }
        }

        #endregion Locks

        #region Traps

        private IEnumerator TurnAutoTurretsOffOrOnCoroutine(BasePlayer player, Building building, bool turnOn, Action<int, int> onComplete)
        {
            int successCount = 0;
            int failCount = 0;

            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is AutoTurret autoTurret)
                {
                    if (_config.PlayerHasToBeOwnerOfHouseEntities && !OwnerOrTeammate(player, autoTurret))
                    {
                        failCount++;
                        continue;
                    }

                    if (turnOn && autoTurret.IsPowered())
                    {
                        autoTurret.InitiateStartup();
                        successCount++;
                    }
                    else if (!turnOn && autoTurret.IsOnline())
                    {
                        autoTurret.InitiateShutdown();
                        successCount++;
                    }

                    if (_config.EnableVisualization)
                    {
                        string action = "Turned Off";
                        if (turnOn)
                        {
                            action = "Turned On";
                        }

                        VisualizeHouseEntity(player, autoTurret.WorldSpaceBounds().position, $"{autoTurret.ShortPrefabName}\n{action}");
                    }
                }
                yield return null;
            }

            if (onComplete != null)
            {
                onComplete.Invoke(successCount, failCount);
            }
        }
        
        private IEnumerator UnloadAutoTurretsCoroutine(BasePlayer player, Building building, Action<int, int> onComplete)
        {
            int successCount = 0;
            int failCount = 0;

            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is AutoTurret autoTurret)
                {
                    if (_config.PlayerHasToBeOwnerOfHouseEntities && !OwnerOrTeammate(player, autoTurret))
                    {
                        failCount++;
                        continue;
                    }

                    if (UnloadTrapFromAmmo(autoTurret.inventory, dropAmmo: true))
                    {
                        successCount++;
                        if (_config.EnableVisualization)
                        {
                            VisualizeHouseEntity(player, autoTurret.WorldSpaceBounds().position, $"{autoTurret.ShortPrefabName}\nUnloaded");
                        }

                        if (autoTurret.IsOnline())
                        {
                            autoTurret.InitiateShutdown();
                        }
                    }
                }

                yield return null;
            }

            if (onComplete != null)
            {
                onComplete.Invoke(successCount, failCount);
            }
        }

        private IEnumerator UnloadTrapsCoroutine(BasePlayer player, Building building, Action<int, int> onComplete)
        {
            int successCount = 0;
            int failCount = 0;

            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is GunTrap || decayEntity is FlameTurret)
                {
                    if (_config.PlayerHasToBeOwnerOfHouseEntities && !OwnerOrTeammate(player, decayEntity))
                    {
                        failCount++;
                        continue;
                    }

                    StorageContainer storageContainer = decayEntity as StorageContainer;
                    if (storageContainer != null)
                    {
                        if (UnloadTrapFromAmmo(storageContainer.inventory, dropAmmo: true))
                        {
                            successCount++;
                            if (_config.EnableVisualization)
                            {
                                VisualizeHouseEntity(player, decayEntity.WorldSpaceBounds().position, $"{decayEntity.ShortPrefabName}\nUnloaded");
                            }
                        }
                    }
                }

                yield return null;
            }

            if (onComplete != null)
            {
                onComplete.Invoke(successCount, failCount);
            }
        }

        private IEnumerator ClearAutoTurretAuthsCoroutine(BasePlayer player, Building building, Action<int, int> onComplete)
        {
            int successCount = 0;
            int failCount = 0;

            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is AutoTurret autoTurret)
                {
                    if (_config.PlayerHasToBeOwnerOfHouseEntities && !OwnerOrTeammate(player, autoTurret))
                    {
                        failCount++;
                        continue;
                    }

                    autoTurret.authorizedPlayers.Clear();
                    successCount++;

                    if (_config.EnableVisualization)
                    {
                        VisualizeHouseEntity(player, autoTurret.WorldSpaceBounds().position, $"{autoTurret.ShortPrefabName}\nAuthorization Cleared");
                    }
                }

                yield return null;
            }

            if (onComplete != null)
            {
                onComplete.Invoke(successCount, failCount);
            }
        }
        
        private IEnumerator SetAutoTurretsAsPeacekeeperOrHostileCoroutine(BasePlayer player, Building building, bool peacekeeper, Action<int, int> onComplete)
        {
            int successCount = 0;
            int failCount = 0;

            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is AutoTurret autoTurret)
                {
                    if (_config.PlayerHasToBeOwnerOfHouseEntities && !OwnerOrTeammate(player, autoTurret))
                    {
                        failCount++;
                        continue;
                    }

                    autoTurret.SetPeacekeepermode(peacekeeper);
                    successCount++;

                    if (_config.EnableVisualization)
                    {
                        string mode = "Hostile";
                        if (peacekeeper)
                        {
                            mode = "Peacekeeper";
                        }

                        VisualizeHouseEntity(player, autoTurret.WorldSpaceBounds().position, $"{autoTurret.ShortPrefabName}\n{mode}");
                    }
                }

                yield return null;
            }

            if (onComplete != null)
            {
                onComplete.Invoke(successCount, failCount);
            }
        }
        
        private bool UnloadTrapFromAmmo(ItemContainer ammoContainer, bool dropAmmo)
        {
            if (ammoContainer == null)
            {
                return false;
            }

            bool unloaded = false;
            List<Item> itemsToUnload = Pool.GetList<Item>();

            foreach (Item item in ammoContainer.itemList)
            {
                if (item != null && item.amount > 0)
                {
                    itemsToUnload.Add(item);
                }
            }

            foreach (Item item in itemsToUnload)
            {
                if (dropAmmo)
                {
                    item.Drop(ammoContainer.dropPosition, ammoContainer.dropVelocity, default(Quaternion));
                }
                else
                {
                    item.Remove();
                }

                unloaded = true;
            }

            Pool.FreeList(ref itemsToUnload);
            return unloaded;
        }

        #endregion Traps

        #region Tool Cupboards

        private IEnumerator ClearCupboardAuthsCoroutine(BasePlayer player, Building building, Action<int, int> onComplete)
        {
            int successCount = 0;
            int failCount = 0;

            foreach (var decayEntity in building.decayEntities)
            {
                if (decayEntity is BuildingPrivlidge cupboard)
                {
                    if (_config.PlayerHasToBeOwnerOfHouseEntities && !OwnerOrTeammate(player, cupboard))
                    {
                        failCount++;
                        continue;
                    }

                    cupboard.authorizedPlayers.Clear();
                    successCount++;

                    if (_config.EnableVisualization)
                    {
                        VisualizeHouseEntity(player, cupboard.WorldSpaceBounds().position, $"{cupboard.ShortPrefabName}\nAuthorization Cleared");
                    }
                }

                yield return null;
            }

            if (onComplete != null)
            {
                onComplete.Invoke(successCount, failCount);
            }
        }

        #endregion Tool Cupboards

        #endregion House Entities Management

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
            /// house.turret peacekeeper
            /// house.turret hostile
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

            if (!PermissionUtil.HasPermission(player, PermissionUtil.DOOR) && !PermissionUtil.HasPermission(player, PermissionUtil.ALL))
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

            BaseEntity entity = GetEntityInSight(player, _config.BuildingDetectionRange);
            if (entity == null)
            {
                SendMessage(player, Lang.NoEntityFound);
                return;
            }

            Building building = TryGetBuildingForEntity(entity, minimumBuildingBlocks: 1, _config.BuildingHasToHaveToolCupboard);
            if (building == null)
            {
                SendMessage(player, Lang.NoBuildingFound);
                return;
            }

            if (_config.PlayerHasToHaveBuildingPrivilege && !PlayerHasBuildingPrivilege(player, building))
            {
                SendMessage(player, Lang.NoAuthorization);
                return;
            }

            if (_config.PlayerHasToHaveBuildingPrivilege && !PlayerHasBuildingPrivilege(player, building) && !PermissionUtil.HasPermission(player, PermissionUtil.BYPASS))
            {
                SendMessage(player, Lang.NoAuthorization);
                return;
            }

            SendMessage(player, Lang.ScanningBuilding);

            CoroutineUtil.StartCoroutine("OpenOrCloseDoors", OpenOrCloseDoorsCoroutine(player, building, open, (successCount, failCount) =>
            {
                if (open)
                {
                    if (successCount > 0)
                        SendMessage(player, Lang.DoorsOpened, successCount);
                    else
                        SendMessage(player, Lang.NoDoorsToOpen);
                }
                else
                {
                    if (successCount > 0)
                        SendMessage(player, Lang.DoorsClosed, successCount);
                    else
                        SendMessage(player, Lang.NoDoorsToClose);
                }

                if (failCount > 0)
                {
                    SendMessage(player, Lang.NoOwnership, failCount);
                }
            }));
        }

        [ChatCommand(Cmd.LOCK)]
        private void cmdLock(BasePlayer player, string cmd, string[] args)
        {
            if (player == null)
                return;

            if (!PermissionUtil.HasPermission(player, PermissionUtil.LOCK) && !PermissionUtil.HasPermission(player, PermissionUtil.ALL))
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

            BaseEntity entity = GetEntityInSight(player, _config.BuildingDetectionRange);
            if (entity == null)
            {
                SendMessage(player, Lang.NoEntityFound);
                return;
            }

            Building building = TryGetBuildingForEntity(entity, minimumBuildingBlocks: 1, _config.BuildingHasToHaveToolCupboard);
            if (building == null)
            {
                SendMessage(player, Lang.NoBuildingFound);
                return;
            }

            if (_config.PlayerHasToHaveBuildingPrivilege && !PlayerHasBuildingPrivilege(player, building) && !PermissionUtil.HasPermission(player, PermissionUtil.BYPASS))
            {
                SendMessage(player, Lang.NoAuthorization);
                return;
            }

            if (args[0] == "unlock")
            {
                SendMessage(player, Lang.ScanningBuilding);
                CoroutineUtil.StartCoroutine("LockOrUnlockLocks", LockOrUnlockLocksCoroutine(player, building, false, (successCount, failCount) =>
                {
                    if (successCount > 0)
                    {
                        SendMessage(player, Lang.LocksUnlocked, successCount);
                    }
                    else
                    {
                        SendMessage(player, Lang.NoLocksToUnlock);
                    }

                    if (failCount > 0)
                    {
                        SendMessage(player, Lang.NoOwnership, failCount);
                    }
                }));
            }
            else if (args[0] == "lock")
            {
                SendMessage(player, Lang.ScanningBuilding);
                CoroutineUtil.StartCoroutine("LockOrUnlockLocks", LockOrUnlockLocksCoroutine(player, building, true, (successCount, failCount) =>
                {
                    if (successCount > 0)
                    {
                        SendMessage(player, Lang.LocksLocked, successCount);
                    }
                    else
                    {
                        SendMessage(player, Lang.NoLocksToLock);
                    }

                    if (failCount > 0)
                    {
                        SendMessage(player, Lang.NoOwnership, failCount);
                    }
                }));
            }
            else if (args[0] == "auth" && args.Length == 2 && args[1] == "clear")
            {
                SendMessage(player, Lang.ScanningBuilding);
                CoroutineUtil.StartCoroutine("ClearCodeLockAuths", ClearCodeLockAuthsCoroutine(player, building, (successCount, failCount) =>
                {
                    if (successCount > 0)
                    {
                        SendMessage(player, Lang.AuthCleared, successCount);
                    }
                    else
                    {
                        SendMessage(player, Lang.NoAuthToClear);
                    }

                    if (failCount > 0)
                    {
                        SendMessage(player, Lang.NoOwnership, failCount);
                    }
                }));
            }
            else if (args[0] == "code" && args.Length == 2)
            {
                string newCode = args[1];
                SendMessage(player, Lang.ScanningBuilding);
                CoroutineUtil.StartCoroutine("ChangeCodeForCodeLocks", ChangeCodeForCodeLocksCoroutine(player, building, newCode, (successCount, failCount) =>
                {
                    if (successCount > 0)
                    {
                        SendMessage(player, Lang.CodeChanged, successCount, newCode);
                    }
                    else
                    {
                        SendMessage(player, Lang.NoCodesToChange);
                    }

                    if (failCount > 0)
                    {
                        SendMessage(player, Lang.NoOwnership, failCount);
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

            if (!PermissionUtil.HasPermission(player, PermissionUtil.TURRET) && !PermissionUtil.HasPermission(player, PermissionUtil.ALL))
            {
                SendMessage(player, Lang.NoPermission);
                return;
            }

            if (args.Length < 1 || (args[0] != "on" && args[0] != "off" && args[0] != "unload" && args[0] != "auth" && args[0] != "peacekeeper" && args[0] != "hostile"))
            {
                SendMessage(player, Lang.InvalidArgs,
                    $"  -<color=#F0E68C> {Cmd.TURRET} on</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} off</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} unload</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} auth clear</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} peacekeeper</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} hostile</color>");
                return;
            }

            BaseEntity entity = GetEntityInSight(player, _config.BuildingDetectionRange);
            if (entity == null)
            {
                SendMessage(player, Lang.NoEntityFound);
                return;
            }

            Building building = TryGetBuildingForEntity(entity, minimumBuildingBlocks: 1, _config.BuildingHasToHaveToolCupboard);
            if (building == null)
            {
                SendMessage(player, Lang.NoBuildingFound);
                return;
            }

            if (_config.PlayerHasToHaveBuildingPrivilege && !PlayerHasBuildingPrivilege(player, building) && !PermissionUtil.HasPermission(player, PermissionUtil.BYPASS))
            {
                SendMessage(player, Lang.NoAuthorization);
                return;
            }

            if (args[0] == "on" || args[0] == "off")
            {
                bool turnOn = args[0] == "on";
                SendMessage(player, Lang.ScanningBuilding);
                CoroutineUtil.StartCoroutine("TurnAutoTurretsOffOrOn", TurnAutoTurretsOffOrOnCoroutine(player, building, turnOn, (successCount, failCount) =>
                {
                    if (turnOn)
                    {
                        if (successCount > 0)
                            SendMessage(player, Lang.TurretsOn, successCount);
                        else
                            SendMessage(player, Lang.NoTurretsToTurnOn);
                    }
                    else
                    {
                        if (successCount > 0)
                            SendMessage(player, Lang.TurretsOff, successCount);
                        else
                            SendMessage(player, Lang.NoTurretsToTurnOff);
                    }

                    if (failCount > 0)
                    {
                        SendMessage(player, Lang.NoOwnership, failCount);
                    }
                }));
            }
            else if (args[0] == "unload")
            {
                SendMessage(player, Lang.ScanningBuilding);
                CoroutineUtil.StartCoroutine("UnloadAutoTurrets", UnloadAutoTurretsCoroutine(player, building, (successCount, failCount) =>
                {
                    if (successCount > 0)
                    {
                        SendMessage(player, Lang.TurretsUnloaded, successCount);
                    }
                    else
                    {
                        SendMessage(player, Lang.NoTurretsToUnload);
                    }

                    if (failCount > 0)
                    {
                        SendMessage(player, Lang.NoOwnership, failCount);
                    }
                }));
            }
            else if (args[0] == "auth" && args.Length == 2 && args[1] == "clear")
            {
                SendMessage(player, Lang.ScanningBuilding);
                CoroutineUtil.StartCoroutine("ClearAutoTurretAuths", ClearAutoTurretAuthsCoroutine(player, building, (successCount, failCount) =>
                {
                    if (successCount > 0)
                    {
                        SendMessage(player, Lang.TurretAuthCleared, successCount);
                    }
                    else
                    {
                        SendMessage(player, Lang.NoTurretAuthToClear);
                    }

                    if (failCount > 0)
                    {
                        SendMessage(player, Lang.NoOwnership, failCount);
                    }
                }));
            }
            else if (args[0] == "peacekeeper")
            {
                SendMessage(player, Lang.ScanningBuilding);
                CoroutineUtil.StartCoroutine("SetAutoTurretsAsPeacekeeperOrHostile", SetAutoTurretsAsPeacekeeperOrHostileCoroutine(player, building, true, (successCount, failCount) =>
                {
                    SendMessage(player, Lang.TurretsPeacekeeper, successCount);

                    if (failCount > 0)
                    {
                        SendMessage(player, Lang.NoOwnership, failCount);
                    }
                }));
            }
            else if (args[0] == "hostile")
            {
                SendMessage(player, Lang.ScanningBuilding);
                CoroutineUtil.StartCoroutine("SetAutoTurretsAsPeacekeeperOrHostile", SetAutoTurretsAsPeacekeeperOrHostileCoroutine(player, building, false, (successCount, failCount) =>
                {
                    SendMessage(player, Lang.TurretsHostile, successCount);

                    if (failCount > 0)
                    {
                        SendMessage(player, Lang.NoOwnership, failCount);
                    }
                }));
            }
            else
            {
                SendMessage(player, Lang.InvalidArgs,
                    $"  -<color=#F0E68C> {Cmd.TURRET} on</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} off</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} unload</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} auth clear</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} peacekeeper</color>\n" +
                    $"  -<color=#F0E68C> {Cmd.TURRET} hostile</color>");
            }
        }

        [ChatCommand(Cmd.CUPBOARD)]
        private void cmdCupboard(BasePlayer player, string cmd, string[] args)
        {
            if (player == null)
                return;

            if (!PermissionUtil.HasPermission(player, PermissionUtil.CUPBOARD) && !PermissionUtil.HasPermission(player, PermissionUtil.ALL))
            {
                SendMessage(player, Lang.NoPermission);
                return;
            }

            if (args.Length != 2 || args[0] != "auth" || args[1] != "clear")
            {
                SendMessage(player, Lang.InvalidArgs,
                    $"  -<color=#F0E68C> {Cmd.CUPBOARD} auth clear</color>");
                return;
            }

            BaseEntity entity = GetEntityInSight(player, _config.BuildingDetectionRange);
            if (entity == null)
            {
                SendMessage(player, Lang.NoEntityFound);
                return;
            }

            Building building = TryGetBuildingForEntity(entity, minimumBuildingBlocks: 1, _config.BuildingHasToHaveToolCupboard);
            if (building == null)
            {
                SendMessage(player, Lang.NoBuildingFound);
                return;
            }

            if (_config.PlayerHasToHaveBuildingPrivilege && !PlayerHasBuildingPrivilege(player, building) && !PermissionUtil.HasPermission(player, PermissionUtil.BYPASS))
            {
                SendMessage(player, Lang.NoAuthorization);
                return;
            }

            SendMessage(player, Lang.ScanningBuilding);
            CoroutineUtil.StartCoroutine("ClearCupboardAuths", ClearCupboardAuthsCoroutine(player, building, (successCount, failCount) =>
            {
                if (successCount > 0)
                {
                    SendMessage(player, Lang.CupboardAuthCleared, successCount);
                }
                else
                {
                    SendMessage(player, Lang.NoCupboardAuthToClear);
                }

                if (failCount > 0)
                {
                    SendMessage(player, Lang.NoOwnership, failCount);
                }
            }));
        }

        [ChatCommand(Cmd.TRAP)]
        private void cmdTrap(BasePlayer player, string cmd, string[] args)
        {
            if (player == null)
                return;

            if (!PermissionUtil.HasPermission(player, PermissionUtil.TRAP) && !PermissionUtil.HasPermission(player, PermissionUtil.ALL))
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

            BaseEntity entity = GetEntityInSight(player, _config.BuildingDetectionRange);
            if (entity == null)
            {
                SendMessage(player, Lang.NoEntityFound);
                return;
            }

            Building building = TryGetBuildingForEntity(entity, minimumBuildingBlocks: 1, _config.BuildingHasToHaveToolCupboard);
            if (building == null)
            {
                SendMessage(player, Lang.NoBuildingFound);
                return;
            }

            if (_config.PlayerHasToHaveBuildingPrivilege && !PlayerHasBuildingPrivilege(player, building) && !PermissionUtil.HasPermission(player, PermissionUtil.BYPASS))
            {
                SendMessage(player, Lang.NoAuthorization);
                return;
            }

            SendMessage(player, Lang.ScanningBuilding);
            CoroutineUtil.StartCoroutine("UnloadTraps", UnloadTrapsCoroutine(player, building, (successCount, failCount) =>
            {
                if (successCount > 0)
                {
                    SendMessage(player, Lang.TrapsUnloaded, successCount);
                }
                else
                {
                    SendMessage(player, Lang.NoTrapsToUnload);
                }

                if (failCount > 0)
                {
                    SendMessage(player, Lang.NoOwnership, failCount);
                }
            }));
        }

        #endregion Commands

        #region Localization

        private class Lang
        {
            public const string NoPermission = "NoPermission";
            public const string InvalidArgs = "InvalidArgs";
            public const string NoEntityFound = "NoEntityFound";
            public const string NoBuildingFound = "NoBuildingFound";
            public const string NoAuthorization = "NoAuthorization";
            public const string NoOwnership = "NoOwnership";
            public const string ScanningBuilding = "ScanningBuilding";
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
            public const string TurretsHostile = "TurretsHostile";
            public const string TurretsPeacekeeper = "TurretsPeacekeeper";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.NoPermission] = "You do not have permission to use this command.",
                [Lang.InvalidArgs] = "Invalid arguments. Usage:\n{0}",
                [Lang.NoEntityFound] = "No entity found in sight.",
                [Lang.NoBuildingFound] = "No building found or the building does not have a tool cupboard.",
                [Lang.NoAuthorization] = "Building privilege is required to perform this action.",
                [Lang.NoOwnership] = "Could not manage <color=#ADFF2F>{0}</color> entities because you are neither the owner nor a member of the owner's team.",
                [Lang.ScanningBuilding] = "Scanning the building. This may take a few seconds, please wait a moment...",
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
                [Lang.TurretsHostile] = "<color=#ADFF2F>{0}</color> auto turrets are now in hostile mode.",
                [Lang.TurretsPeacekeeper] = "<color=#ADFF2F>{0}</color> auto turrets are now in peacekeeper mode."
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