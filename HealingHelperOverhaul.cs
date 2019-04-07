using Harmony;
using RoR2;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using System.Collections.ObjectModel;
using UnityEngine.Networking;
using UnityModManagerNet;

namespace Frogtown
{
    public class HealingHelperMain
    {
        /// <summary>
        /// Dictionary of body names for characters that have been turned into drones.
        /// </summary>
        public static Dictionary<string, string> originalBodyNames = new Dictionary<string, string>();

        public static bool enabled;
        public static bool gameover;
        public static UnityModManager.ModEntry modEntry;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            var harmony = HarmonyInstance.Create("com.frog.healinghelperoverhaul");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            HealingHelperMain.modEntry = modEntry;
            enabled = true;
            modEntry.OnToggle = OnToggle;
            return true;
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            enabled = value;
            FrogtownShared.ModToggled(value);
            return true;
        }

        /// <summary>
        /// Restores user to their original prefab
        /// </summary>
        /// <param name="name"></param>
        public static void RestoreCharacterPrefab(string name)
        {
            PlayerCharacterMasterController player = FrogtownShared.GetPlayerWithName(name);
            if (player != null)
            {
                if (originalBodyNames.TryGetValue(name, out string oldBodyName))
                {
                    var prefab = BodyCatalog.FindBodyPrefab(oldBodyName);
                    if (prefab != null)
                    {
                        player.master.bodyPrefab = prefab;
                    }
                    else
                    {
                        FrogtownShared.SendChat("No prefab for \"" + oldBodyName + "\"");
                    }
                }
            }
        }

        /// <summary>
        /// Restores all users to their original prefabs.
        /// </summary>
        public static void RestoreCharacterPrefabsAndKill()
        {
            foreach (string name in originalBodyNames.Keys)
            {
                PlayerCharacterMasterController player = FrogtownShared.GetPlayerWithName(name);
                if (player != null)
                {
                    if (originalBodyNames.TryGetValue(name, out string oldBodyName))
                    {
                        var prefab = BodyCatalog.FindBodyPrefab(oldBodyName);
                        if (prefab != null)
                        {
                            FrogtownShared.ChangePrefab(name, prefab);
                            player.master.Invoke("TrueKill", 0.1f);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Copied logic, except isntead of checking for "PreventGameOver" we check for if they are a healing drone or not.
    /// </summary>
    [HarmonyPatch(typeof(RoR2.Stage))]
    [HarmonyPatch("FixedUpdate")]
    [HarmonyPatch(new Type[] { })]
    class StageTickPatch
    {
        private static HashSet<string> botPlayers = new HashSet<string>();

        public static void Clearbots()
        {
            botPlayers.Clear();
        }

        public static void PlayerIsBot(string player)
        {
            botPlayers.Add(player);
        }

        static void Postfix(Stage __instance)
        {
            if (HealingHelperMain.gameover)
            {
                return;
            }

            //Still end even when disabled in case it was turned off mid round.
            if (NetworkServer.active)
            {
                var sAnyPlayer = Traverse.Create(__instance).Field<bool>("spawnedAnyPlayer");
                if (sAnyPlayer.Value && float.IsInfinity(__instance.stageAdvanceTime) && !Run.instance.isGameOverServer)
                {
                    ReadOnlyCollection<PlayerCharacterMasterController> instances = PlayerCharacterMasterController.instances;
                    bool flag = false;
                    for (int i = 0; i < instances.Count; i++)
                    {
                        PlayerCharacterMasterController player = instances[i];
                        if (player.isConnected && !botPlayers.Contains(player.networkUser.GetNetworkPlayerName().GetResolvedName()))
                        {
                            flag = true;
                            break;
                        }
                    }
                    if (!flag)
                    {
                        HealingHelperMain.RestoreCharacterPrefabsAndKill();
                        HealingHelperMain.gameover = true;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Respawns as healing drone and saves original prefab name.
    /// </summary>
    [HarmonyPatch(typeof(RoR2.CharacterMaster))]
    [HarmonyPatch("OnBodyDeath")]
    [HarmonyPatch(new Type[] { })]
    class DeathPatch
    {
        static void Postfix(CharacterMaster __instance)
        {
            if (HealingHelperMain.gameover)
            {
                return;
            }
            PlayerCharacterMasterController player = __instance.GetComponent<PlayerCharacterMasterController>();
            if (player != null)
            {
                if (!__instance.preventGameOver)
                {
                    if (HealingHelperMain.enabled)
                    {
                        var name = player.networkUser.GetNetworkPlayerName().GetResolvedName();
                        HealingHelperMain.originalBodyNames.Add(name, player.master.bodyPrefab.name);
                        var prefab = BodyCatalog.FindBodyPrefab("Drone2Body");
                        FrogtownShared.ChangePrefab(name, prefab);
                    }
                    //Still log they are a bot incase the mod was disabled in the middle of the round, so that the game can end properly.
                    StageTickPatch.PlayerIsBot(player.networkUser.GetNetworkPlayerName().GetResolvedName());
                }
            }
        }
    }

    /// <summary>
    /// Clear out body names so that if users change characters between rounds we respect the change.
    /// </summary>
    [HarmonyPatch(typeof(RoR2.Run))]
    [HarmonyPatch("Start")]
    [HarmonyPatch(new Type[] { })]
    class RunAdvanceStagePatch
    {
        static void Postfix()
        {
            HealingHelperMain.originalBodyNames.Clear();
            HealingHelperMain.gameover = false;
            StageTickPatch.Clearbots();
        }
    }

    /// <summary>
    /// Restores the original prefab on stage change.
    /// </summary>
    [HarmonyPatch(typeof(RoR2.Stage))]
    [HarmonyPatch("RespawnCharacter")]
    [HarmonyPatch(new Type[] { typeof(CharacterMaster) })]
    class StagePatch
    {
        static void Prefix(CharacterMaster characterMaster)
        {
            if (!HealingHelperMain.enabled)
            {
                return;
            }
            PlayerCharacterMasterController player = characterMaster.GetComponent<PlayerCharacterMasterController>();
            string name = player.networkUser.GetNetworkPlayerName().GetResolvedName();
            HealingHelperMain.RestoreCharacterPrefab(name);
        }
    }
}