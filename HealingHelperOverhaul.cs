using BepInEx;
using Harmony;
using RoR2;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine.Networking;

namespace Frogtown
{
    [BepInDependency("com.frogtown.shared")]
    [BepInPlugin("com.frogtown.healinghelper", "Healing Helper", "1.0")]
    public class HealingHelperMain : BaseUnityPlugin
    {
        public ModDetails modDetails;
        public void Awake()
        {
            modDetails = new ModDetails("com.frogtown.healinghelper");
            On.RoR2.Stage.FixedUpdate += (orig, instance) =>
            {
                StageFixedUpdatePrefix(instance);
                orig(instance);
            };

            On.RoR2.CharacterMaster.OnBodyDeath += (orig, instance) =>
            {
                orig(instance);
                CharacterMasterOnBodyDeathPostfix(instance);
            };

            On.RoR2.Run.Start += (orig, instance) =>
            {
                orig(instance);
                RunStartPostfix();
            };

            On.RoR2.Stage.RespawnCharacter += (orig, instance, characterMaster) =>
            {
                StageRespawnCharacterPrefix(characterMaster);
                orig(instance, characterMaster);
            };
        }

        /// <summary>
        /// Dictionary of body names for characters that have been turned into drones.
        /// </summary>
        public Dictionary<string, string> originalBodyNames = new Dictionary<string, string>();

        /// <summary>
        /// Which players are currently bots
        /// </summary>
        private HashSet<string> botPlayers = new HashSet<string>();

        /// <summary>
        /// true if all players are bots
        /// </summary>
        public bool gameover;

        /// <summary>
        /// Restores user to their original prefab
        /// </summary>
        /// <param name="name"></param>
        public void RestoreCharacterPrefab(string name)
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
        public void RestoreCharacterPrefabsAndKill()
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

        private void StageFixedUpdatePrefix(Stage instance)
        {
            if (gameover)
            {
                return;
            }

            //Still end even when disabled in case it was turned off mid round.
            if (NetworkServer.active)
            {
                var sAnyPlayer = Traverse.Create(instance).Field("spawnedAnyPlayer").GetValue() as bool?;
                if (sAnyPlayer.HasValue && sAnyPlayer.Value && float.IsInfinity(instance.stageAdvanceTime) && !Run.instance.isGameOverServer)
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
                        RestoreCharacterPrefabsAndKill();
                        gameover = true;
                    }
                }
            }
        }

        private void CharacterMasterOnBodyDeathPostfix(CharacterMaster instance)
        {
            if (gameover)
            {
                return;
            }
            PlayerCharacterMasterController player = instance.GetComponent<PlayerCharacterMasterController>();
            if (player != null)
            {
                if (!instance.preventGameOver)
                {
                    if (modDetails.enabled)
                    {
                        var name = player.networkUser.GetNetworkPlayerName().GetResolvedName();
                        originalBodyNames.Add(name, player.master.bodyPrefab.name);
                        var prefab = BodyCatalog.FindBodyPrefab("Drone2Body");
                        FrogtownShared.ChangePrefab(name, prefab);
                    }
                    //Still log they are a bot incase the mod was disabled in the middle of the round, so that the game can end properly.
                    botPlayers.Add(player.networkUser.GetNetworkPlayerName().GetResolvedName());
                }
            }
        }

        private void RunStartPostfix()
        {
            originalBodyNames.Clear();
            gameover = false;
            botPlayers.Clear();
        }

        private void StageRespawnCharacterPrefix(CharacterMaster characterMaster)
        {
            if (!modDetails.enabled)
            {
                return;
            }
            PlayerCharacterMasterController player = characterMaster.GetComponent<PlayerCharacterMasterController>();
            string name = player.networkUser.GetNetworkPlayerName().GetResolvedName();
            RestoreCharacterPrefab(name);
        }
    }
}