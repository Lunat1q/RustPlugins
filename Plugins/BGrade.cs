using System;
using System.Linq;
using System.Collections.Generic;
using Facepunch.Math;
using Network;
using Rust;
using UnityEngine;

using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Plugins.BGradeExt;

namespace Oxide.Plugins
{

    [Info("BGrade", "Ryan / Rustoria.co, 4seti", "1.2.0")]
    [Description("Auto update building blocks when placed")]
    public class BGrade : RustPlugin
    {
        public static BGrade Instance;

        #region Declaration


        private ListHashSet<string> _registeredPermissions = new ListHashSet<string>();
        private readonly Dictionary<Vector3, int> _lastAttacked = new Dictionary<Vector3, int>();

        private readonly Dictionary<BuildingGrade.Enum, ulong[]> _gradesSkin =
            new Dictionary<BuildingGrade.Enum, ulong[]>
            {
                { BuildingGrade.Enum.Wood, new ulong[] { 10232 } }, // LegacyWood
                { BuildingGrade.Enum.Stone, new ulong[] { 10220, 10223, 10225 } }, // Adobe, Brick, Brutalist
                { BuildingGrade.Enum.Metal, new ulong[] { 10221 } }, // ShippingContainer
                { BuildingGrade.Enum.TopTier, new ulong[] { } }
            };

        private readonly Dictionary<BuildingGrade.Enum, string[]> _skinNames =
            new Dictionary<BuildingGrade.Enum, string[]>
            {
                { BuildingGrade.Enum.Wood, new[] { Labels.LegacyWood } },
                { BuildingGrade.Enum.Stone, new[] { Labels.Adobe, Labels.Brick, Labels.Brutalist } },
                { BuildingGrade.Enum.Metal, new[] { Labels.ShippingContainer } },
                { BuildingGrade.Enum.TopTier, new string[] { } }
            };
        #endregion
        
        #region Lang
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Labels.Permission] = "You don't have permission to use that command",

                [Labels.ErrorInvalidArgs] = "Invalid arguments, please use /{0} help",
                [Labels.ErrorResources] = "You don't have enough resources to upgrade.",
                [Labels.ErrorInvalidTime] = "Please enter a valid time. '<color=orange>{0}</color>' is not recognised as a number.",
                [Labels.ErrorTimerTooLong] = "Please enter a time that is below the value of <color=orange>{0}</color>.",

                [Labels.NoticeSetGrade] = "Automatic upgrading is now set to grade <color=orange>{0}</color>.",
                [Labels.NoticeSetGradeWithSkin] = "Automatic upgrading is now set to grade <color=orange>{0}</color>, with skin <color=green>{1}</color>.",
                [Labels.NoticeSetTime] = "The disable timer is now set to <color=orange>{0}</color>.",
                [Labels.NoticeDisabled] = "Automatic upgrading is now disabled.",
                [Labels.NoticeDisabledAuto] = "Automatic upgrading has been automatically disabled.",
                [Labels.NoticeTime] = "It'll automatically disable in <color=orange>{0}</color> seconds.",

                [Labels.CommandHelp] = "<color=orange><size=16>BGrade Command Usages</size></color>",
                [Labels.CommandHelpPrefix + Labels.ZeroGrade] = "/{0} 0 - Disables BGrade",
                [Labels.CommandHelpPrefix + Labels.FirstGrade] = "/{0} 1 - Upgrades to Wood upon placement",
                [Labels.CommandHelpPrefix + Labels.SecondGrade] = "/{0} 2 - Upgrades to Stone upon placement",
                [Labels.CommandHelpPrefix + Labels.ThirdGrade] = "/{0} 3 - Upgrades to Metal upon placement",
                [Labels.CommandHelpPrefix + Labels.FourthGrade] = "/{0} 4 - Upgrades to Armoured upon placement",
                [Labels.CommandHelpSkin] = "/{0} <grade> <skin> - Time until BGrade is disabled",
                [Labels.CommandHelpSkinAvailability] = "Skins available: Wood 0 - {0}, Stone 0 - {1}, Metal 0 - {2}",
                [Labels.CommandHelpT] = "/{0} t <seconds> - Time until BGrade is disabled",

                [Labels.CommandSettings] = "<color=orange><size=16>Your current settings</size></color>",
                [Labels.CommandSettingsTimer] = "Timer: <color=orange>{0}</color> seconds",
                [Labels.CommandSettingsGrade] = "Grade: <color=orange>{0}</color>",

                [Labels.WordsDisabled] = "disabled",
                [Labels.WordsLabelPrefix + Labels.Adobe] = "Adobe",
                [Labels.WordsLabelPrefix + Labels.Brick] = "Brick",
                [Labels.WordsLabelPrefix + Labels.Brutalist] = "Brutalist",
                [Labels.WordsLabelPrefix + Labels.LegacyWood] = "Legacy Wood",
                [Labels.WordsLabelPrefix + Labels.ShippingContainer] = "Shipping Container",
            }, this);
        }
        #endregion

        #region Config

        private bool ConfigChanged;

        // Timer settings
        private bool AllowTimer;
        private int MaxTimer;
        private bool UpgradeAnimation;
        private int DefaultTimer;

        // Last attack settings
        private bool CheckLastAttack;
        private int UpgradeCooldown;

        // Command settings
        private List<string> ChatCommands;
        private List<string> ConsoleCommands;

        // Refund settings
        private bool RefundOnBlock;

        // Player Component settings
        private bool DestroyOnDisconnect;

        public BGrade()
        {
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Generating default configuration file...");
        }

        private void InitConfig()
        {
            AllowTimer = GetConfig(true, "Timer Settings", "Enabled");
            DefaultTimer = GetConfig(30, "Timer Settings", "Default Timer");
            MaxTimer = GetConfig(180, "Timer Settings", "Max Timer");
            UpgradeAnimation = GetConfig(true, "Behavior", "Play Upgrade Animation");
            ChatCommands = GetConfig(new List<string>
            {
                "bgrade",
                "grade"
            }, "Command Settings", "Chat Commands");
            ConsoleCommands = GetConfig(new List<string>
            {
                "bgrade.up"
            }, "Command Settings", "Console Commands");
            CheckLastAttack = GetConfig(true, "Building Attack Settings", "Enabled");
            UpgradeCooldown = GetConfig(30, "Building Attack Settings", "Cooldown Time");
            RefundOnBlock = GetConfig(true, "Refund Settings", "Refund on Block");
            DestroyOnDisconnect = GetConfig(false, "Destroy Data on Player Disconnect (for high pop servers)");

            if (ConfigChanged)
            {
                PrintWarning("Updated configuration file with new/changed values.");
                SaveConfig();
            }
        }

        private T GetConfig<T>(T defaultVal, params string[] path)
        {
            var data = Config.Get(path);
            if (data != null)
            {
                return Config.ConvertValue<T>(data);
            }

            Config.Set(path.Concat(new object[] { defaultVal }).ToArray());
            ConfigChanged = true;
            return defaultVal;
        }

        #endregion

        #region Methods

        private void RegisterPermissions()
        {
            _registeredPermissions = new ListHashSet<string>();

            for (var i = 1; i < 5; i++)
            {
                RegisterPermission(Name.ToLower() + "." + i);
            }

            RegisterPermission(Name.ToLower() + "." + Labels.NoResPermissionName);
            RegisterPermission(Name.ToLower() + "." + Labels.AllPermissionName);
            RegisterPermission(Name.ToLower() + "." + Labels.SkinsPermissionName);
        }

        private void RegisterPermission(string permissionName)
        {
            if (!_registeredPermissions.Contains(permissionName))
            {
                _registeredPermissions.Add(permissionName);
            }

            permission.RegisterPermission(permissionName, this);
        }

        private void RegisterCommands()
        {
            foreach (var command in ChatCommands)
            {
                cmd.AddChatCommand(command, this, BGradeCommand);
            }

            foreach (var command in ConsoleCommands)
            {
                cmd.AddConsoleCommand(command, this, nameof(BGradeUpCommand));
            }
        }

        private void DestroyAll<T>() where T : MonoBehaviour
        {
            foreach (var type in UnityEngine.Object.FindObjectsOfType<T>())
            {
                UnityEngine.Object.Destroy(type);
            }
        }

        private void DealWithHookResult(BasePlayer player,
                                        BuildingBlock buildingBlock,
                                        int hookResult,
                                        GameObject gameObject)
        {
            if (hookResult <= 0)
            {
                return;
            }

            if (RefundOnBlock)
            {
                foreach (var itemToGive in buildingBlock.BuildCost())
                {
                    player.GiveItem(ItemManager.CreateByItemID(itemToGive.itemid, (int)itemToGive.amount));
                }
            }

            gameObject.GetComponent<BaseEntity>().Kill();
        }

        private string TakeResources(BasePlayer player,
                                     int playerGrade,
                                     BuildingBlock buildingBlock,
                                     out Dictionary<int, int> items)
        {
            var itemsToTake = new Dictionary<int, int>();

            List<ItemAmount> costToBuild = null;
            foreach (var grade in buildingBlock.blockDefinition.grades)
            {
                if (grade.gradeBase.type == (BuildingGrade.Enum)playerGrade)
                {
                    costToBuild = grade.CostToBuild();
                    break;
                }
            }

            if (costToBuild == null)
            {
                PrintError($"COULDN'T FIND COST TO BUILD WITH GRADE: {playerGrade} FOR {buildingBlock.PrefabName}");
                items = itemsToTake;
                return Labels.ErrorResources.Lang(player.UserIDString);
            }

            foreach (var itemAmount in costToBuild)
            {
                if (!itemsToTake.ContainsKey(itemAmount.itemid))
                {
                    itemsToTake.Add(itemAmount.itemid, 0);
                }

                itemsToTake[itemAmount.itemid] += (int)itemAmount.amount;
            }

            var canAfford = true;
            foreach (var itemToTake in itemsToTake)
            {
                if (!player.HasItemAmount(itemToTake.Key, itemToTake.Value))
                {
                    canAfford = false;
                }
            }

            items = itemsToTake;
            return canAfford ? null : Labels.ErrorResources.Lang(player.UserIDString);
        }

        private void CheckLastAttacked()
        {
            foreach (var lastAttackEntry in _lastAttacked.ToList())
            {
                if (!WasAttackedRecently(lastAttackEntry.Key))
                {
                    _lastAttacked.Remove(lastAttackEntry.Key);
                }
            }
        }

        private bool WasAttackedRecently(Vector3 position)
        {
            int time;
            if (!_lastAttacked.TryGetValue(position, out time))
            {
                return false;
            }

            if (time < Epoch.Current)
            {
                return true;
            }

            return false;
        }

        #endregion

        #region Hooks

        private void Init()
        {
            Instance = this;

            InitConfig();
            RegisterCommands();
            RegisterPermissions();

            if (!CheckLastAttack)
            {
                Unsubscribe(nameof(OnEntityDeath));
                Unsubscribe(nameof(OnServerSave));
            }

            if (!DestroyOnDisconnect)
            {
                Unsubscribe(nameof(OnPlayerDisconnected));
            }
        }

        private void OnServerSave()
        {
            CheckLastAttacked();
        }

        private void Unload()
        {
            Instance = null;
            DestroyAll<BGradePlayer>();
            BGradePlayer.Players.Clear();
        }

        private void OnEntityBuilt(Planner plan, GameObject gameObject)
        {
            var player = plan?.GetOwnerPlayer();
            if (player == null)
            {
                return;
            }

            if (plan.isTypeDeployable)
            {
                return;
            }

            var buildingBlock = gameObject.GetComponent<BuildingBlock>();
            if (buildingBlock == null)
            {
                return;
            }

            if (!player.CanBuild())
            {
                return;
            }

            if (!player.HasAnyPermission(_registeredPermissions))
            {
                return;
            }

            BGradePlayer bgradePlayer;
            if (!BGradePlayer.Players.TryGetValue(player, out bgradePlayer))
            {
                return;
            }

            var playerGrade = bgradePlayer.GetGrade();
            if (playerGrade == 0)
            {
                return;
            }

            if (!player.HasPluginPerm(Labels.AllPermissionName) && !player.HasPluginPerm(playerGrade.ToString()))
            {
                return;
            }

            var hookCall = Interface.Call(Labels.CanBGradeHookName, player, playerGrade, buildingBlock, plan);

            if (hookCall is int)
            {
                DealWithHookResult(player, buildingBlock, (int)hookCall, gameObject);
                return;
            }

            if (playerGrade < (int)buildingBlock.grade || buildingBlock.blockDefinition.grades[playerGrade] == null)
            {
                return;
            }

            if (CheckLastAttack && WasAttackedRecently(buildingBlock.transform.position))
            {
                return;
            }

            if (Interface.Call(Labels.OnStructureUpdateHookName, buildingBlock, player, (BuildingGrade.Enum)playerGrade) != null)
            {
                return;
            }

            if (!player.HasPluginPerm(Labels.NoResPermissionName))
            {
                Dictionary<int, int> itemsToTake;
                var resourceResponse = TakeResources(player, playerGrade, buildingBlock, out itemsToTake);
                if (!string.IsNullOrEmpty(resourceResponse))
                {
                    player.ChatMessage(resourceResponse);
                    return;
                }

                foreach (var itemToTake in itemsToTake)
                {
                    player.TakeItem(itemToTake.Key, itemToTake.Value);
                }
            }

            if (AllowTimer)
            {
                bgradePlayer.UpdateTime();
            }

            ulong skinId = 0;
            if (player.HasPluginPerm(Labels.SkinsPermissionName))
            {
                skinId = bgradePlayer.GetSkin();
            }

            buildingBlock.SetGrade((BuildingGrade.Enum)playerGrade);
            buildingBlock.SetHealthToMax();
            buildingBlock.StartBeingRotatable();
            buildingBlock.SendNetworkUpdate();
            
            if (UpgradeAnimation)
            {
                var target = new RpcTarget
                {
                    Function = Labels.DoUpgradeEffectFunctionName,
                    Connections = new SendInfo(plan.GetOwnerConnection())
                };
                buildingBlock.ClientRPC(target, (int)buildingBlock.grade, skinId);
            }

            buildingBlock.skinID = skinId;
            buildingBlock.UpdateSkin();
            buildingBlock.ResetUpkeepTime();
            buildingBlock.GetBuilding()?.Dirty();
        }

        private object OnPayForPlacement(BasePlayer player, Planner planner, Construction component)
        {
            if (planner.isTypeDeployable)
            {
                return null;
            }

            if (!BGradePlayer.Players.ContainsKey(player))
            {
                return null;
            }

            if (!player.HasPluginPerm(Labels.NoResPermissionName))
            {
                return null;
            }

            var bgradePlayer = BGradePlayer.Players[player];
            if (bgradePlayer.GetGrade() == 0)
            {
                return null;
            }

            return false;
        }

        private void OnEntityDeath(BuildingBlock buildingBlock, HitInfo info)
        {
            var attacker = info?.InitiatorPlayer;
            if (attacker == null)
            {
                return;
            }

            if (info.damageTypes.GetMajorityDamageType() == DamageType.Explosion)
            {
                _lastAttacked[buildingBlock.transform.position] = Epoch.Current + UpgradeCooldown;
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            BGradePlayer bgradePlayer;
            if (!BGradePlayer.Players.TryGetValue(player, out bgradePlayer))
            {
                return;
            }

            bgradePlayer.Destroy();
        }

        #endregion

        #region Commands

        private void BGradeCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.HasAnyPermission(_registeredPermissions))
            {
                player.ChatMessage(Labels.Permission.Lang(player.UserIDString));
                return;
            }

            if (args.Length == 0)
            {
                player.ChatMessage(Labels.ErrorInvalidArgs.Lang(player.UserIDString, command));
                return;
            }

            var chatMsgs = new List<string>();

            switch (args[0].ToLower())
            {
                case Labels.ZeroGrade:
                {
                    player.ChatMessage(Labels.NoticeDisabled.Lang(player.UserIDString));
                    BGradePlayer bgradePlayer;
                    if (BGradePlayer.Players.TryGetValue(player, out bgradePlayer))
                    {
                        bgradePlayer.DestroyTimer();
                        bgradePlayer.SetGrade(0);
                        bgradePlayer.SetSkin(0);
                    }

                    return;
                }

                case Labels.FirstGrade:
                case Labels.SecondGrade:
                case Labels.ThirdGrade:
                case Labels.FourthGrade:
                {
                    if (!player.HasPluginPerm(Labels.AllPermissionName) && !player.HasPluginPerm(args[0]))
                    {
                        player.ChatMessage(Labels.Permission.Lang(player.UserIDString));
                        return;
                    }

                    var grade = Convert.ToInt32(args[0]);
                    ulong skinGameId = 0;
                    string skinName;
                    if (args.Length > 1 && player.HasPluginPerm(Labels.SkinsPermissionName))
                    {
                        uint skinIndex;
                        if (!uint.TryParse(args[1], out skinIndex))
                        {
                            player.ChatMessage(Labels.ErrorInvalidArgs.Lang(player.UserIDString, command));
                            return;
                        }

                        var supportedSkins = _gradesSkin[(BuildingGrade.Enum)grade];
                        if (supportedSkins.Length >= skinIndex)
                        {
                            skinGameId = skinIndex == 0 ? 0L : supportedSkins[skinIndex - 1];
                            skinName = (Labels.WordsLabelPrefix + _skinNames[(BuildingGrade.Enum)grade][skinIndex - 1]).Lang(player.UserIDString);
                        }
                        else
                        {
                            skinName = Labels.DefaultSkinName;
                        }
                        chatMsgs.Add(Labels.NoticeSetGradeWithSkin.Lang(player.UserIDString, grade, skinName));
                    }
                    else
                    {
                        chatMsgs.Add(Labels.NoticeSetGrade.Lang(player.UserIDString, grade));
                    }

                    BGradePlayer bgradePlayer;
                    if (!BGradePlayer.Players.TryGetValue(player, out bgradePlayer))
                    {
                        bgradePlayer = player.gameObject.AddComponent<BGradePlayer>();
                    }

                    bgradePlayer.SetGrade(grade);
                    bgradePlayer.SetSkin(skinGameId);
                    var time = bgradePlayer.GetTime();

                    if (AllowTimer && time > 0)
                    {
                        chatMsgs.Add(Labels.NoticeTime.Lang(player.UserIDString, time));
                    }

                    player.ChatMessage(string.Join("\n", chatMsgs.ToArray()));
                    return;
                }

                case "t":
                {
                    if (!AllowTimer)
                    {
                        return;
                    }

                    if (args.Length == 1)
                    {
                        goto default;
                    }

                    int time;
                    if (!int.TryParse(args[1], out time) || time <= 0)
                    {
                        player.ChatMessage(Labels.ErrorInvalidTime.Lang(player.UserIDString, args[1]));
                        return;
                    }

                    if (time > MaxTimer)
                    {
                        player.ChatMessage(Labels.ErrorTimerTooLong.Lang(player.UserIDString, MaxTimer));
                        return;
                    }

                    BGradePlayer bgradePlayer;
                    if (!BGradePlayer.Players.TryGetValue(player, out bgradePlayer))
                    {
                        bgradePlayer = player.gameObject.AddComponent<BGradePlayer>();
                    }

                    player.ChatMessage(Labels.NoticeSetTime.Lang(player.UserIDString, time));
                    bgradePlayer.SetTime(time);
                    return;
                }

                case Labels.HelpCommandName:
                {
                    chatMsgs.Add(Labels.CommandHelp.Lang(player.UserIDString));
                    if (AllowTimer)
                    {
                        chatMsgs.Add(Labels.CommandHelpT.Lang(player.UserIDString, command));
                        chatMsgs.Add((Labels.CommandHelpPrefix + Labels.ZeroGrade).Lang(player.UserIDString, command));
                    }

                    for (var i = 1; i < 5; i++)
                    {
                        if (player.HasPluginPerm(i.ToString()) || player.HasPluginPerm(Labels.AllPermissionName))
                        {
                            chatMsgs.Add($"{Labels.CommandHelpPrefix}{i}".Lang(player.UserIDString, command));
                        }
                    }

                    if (player.HasPluginPerm(Labels.SkinsPermissionName))
                    {
                        chatMsgs.Add(Labels.CommandHelpSkin.Lang(player.UserIDString, command));
                        chatMsgs.Add(Labels.CommandHelpSkinAvailability.Lang(player.UserIDString, _gradesSkin[BuildingGrade.Enum.Wood].Length, _gradesSkin[BuildingGrade.Enum.Stone].Length, _gradesSkin[BuildingGrade.Enum.Metal].Length));
                    }

                    if (chatMsgs.Count <= 3 && !player.HasPluginPerm(Labels.AllPermissionName))
                    {
                        player.ChatMessage(Labels.Permission.Lang(player.UserIDString));
                        return;
                    }

                    BGradePlayer bgradePlayer;
                    if (BGradePlayer.Players.TryGetValue(player, out bgradePlayer))
                    {
                        chatMsgs.Add(Labels.CommandSettings.Lang(player.UserIDString));
                        if (AllowTimer)
                        {
                            chatMsgs.Add(Labels.CommandSettingsTimer.Lang(player.UserIDString, bgradePlayer.GetTime(false)));
                        }

                        var fetchedGrade = bgradePlayer.GetGrade();
                        var fetchedSkin = bgradePlayer.GetSkin();
                        chatMsgs.Add(Labels.CommandSettingsGrade.Lang(player.UserIDString,
                            fetchedGrade == 0 ? Labels.WordsDisabled.Lang(player.UserIDString) : fetchedGrade.ToString()));
                    }

                    player.ChatMessage(string.Join("\n", chatMsgs.ToArray()));
                    return;
                }

                default:
                {
                    player.ChatMessage(Labels.ErrorInvalidArgs.Lang(player.UserIDString, command));
                    return;
                }
            }
        }

        private void BGradeUpCommand(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null)
            {
                return;
            }

            if (!player.HasAnyPermission(_registeredPermissions))
            {
                player.ChatMessage(Labels.Permission.Lang(player.UserIDString));
                return;
            }

            BGradePlayer bgradePlayer;
            if (!BGradePlayer.Players.TryGetValue(player, out bgradePlayer))
            {
                bgradePlayer = player.gameObject.AddComponent<BGradePlayer>();
            }

            var grade = bgradePlayer.GetGrade() + 1;
            var count = 0;

            if (!player.HasPluginPerm(Labels.AllPermissionName))
            {
                while (!player.HasPluginPerm(grade.ToString()))
                {
                    var newGrade = grade++;
                    if (newGrade > 4)
                    {
                        grade = 1;
                    }

                    if (count > bgradePlayer.GetGrade() + 4)
                    {
                        player.ChatMessage(Labels.Permission.Lang(player.UserIDString));
                        return;
                    }
                }
            }
            else if (grade > 4)
            {
                grade = 1;
            }

            var chatMsgs = new List<string>();
            bgradePlayer.SetGrade(grade);
            bgradePlayer.SetSkin(0);
            var time = bgradePlayer.GetTime();

            chatMsgs.Add(Labels.NoticeSetGrade.Lang(player.UserIDString, grade));
            if (AllowTimer && time > 0)
            {
                chatMsgs.Add(Labels.NoticeTime.Lang(player.UserIDString, time));
            }

            player.ChatMessage(string.Join("\n", chatMsgs));
        }

        #endregion

        #region BGrade Player

        private class BGradePlayer : FacepunchBehaviour
        {
            public static readonly Dictionary<BasePlayer, BGradePlayer> Players = new Dictionary<BasePlayer, BGradePlayer>();

            private int _grade;

            private BasePlayer _player;
            private ulong _skin;
            private int _time;
            private Timer _timer;

            public void Awake()
            {
                var attachedPlayer = GetComponent<BasePlayer>();
                if (attachedPlayer == null || !attachedPlayer.IsConnected)
                {
                    return;
                }

                _player = attachedPlayer;
                Players[_player] = this;

                _time = GetTime(false);
            }

            public int GetTime(bool updateTime = true)
            {
                if (!Instance.AllowTimer)
                {
                    return 0;
                }

                if (updateTime)
                {
                    UpdateTime();
                }

                return _time != 0 ? _time : Instance.DefaultTimer;
            }

            public void UpdateTime()
            {
                if (_time <= 0)
                {
                    return;
                }

                DestroyTimer();

                SetTimer(Instance.timer.Once(_time, () =>
                {
                    _grade = 0;
                    DestroyTimer();
                    _player.ChatMessage(Labels.NoticeDisabledAuto.Lang(_player.UserIDString));
                }));
            }

            public int GetGrade()
            {
                return _grade;
            }

            public ulong GetSkin()
            {
                return _skin;
            }

            private void SetTimer(Timer timer)
            {
                _timer = timer;
            }

            public void SetGrade(int newGrade)
            {
                _grade = newGrade;
            }

            public void SetSkin(ulong newSkin)
            {
                _skin = newSkin;
            }

            public void SetTime(int newTime)
            {
                _time = newTime;
            }

            public void DestroyTimer()
            {
                _timer?.Destroy();
                _timer = null;
            }

            public void Destroy()
            {
                Destroy(this);
            }

            public void OnDestroy()
            {
                if (Players.ContainsKey(_player))
                {
                    Players.Remove(_player);
                }
            }
        }

        #endregion


        private static class Labels
        {

            internal const string HelpCommandName = "help";
            internal const string OnStructureUpdateHookName = "OnStructureUpgrade";
            internal const string DoUpgradeEffectFunctionName = "DoUpgradeEffect";
            internal const string SkinsPermissionName = "skins";
            internal const string NoResPermissionName = "nores";
            internal const string AllPermissionName = "all";
            internal const string CanBGradeHookName = "CanBGrade";
            internal const string ErrorResources = "Error.Resources";
            internal const string Permission = "Permission";
            internal const string NoticeDisabled = "Notice.Disabled";
            internal const string WordsLabelPrefix = "Words.";
            internal const string NoticeSetGradeWithSkin = "Notice.SetGradeWithSkin";
            internal const string NoticeSetGrade = "Notice.SetGrade";
            internal const string DefaultSkinName = "Default";
            internal const string NoticeTime = "Notice.Time";
            internal const string ErrorInvalidTime = "Error.InvalidTime";
            internal const string ErrorTimerTooLong = "Error.TimerTooLong";
            internal const string NoticeSetTime = "Notice.SetTime";
            internal const string CommandHelp = "Command.Help";
            internal const string CommandHelpT = "Command.Help.T";
            internal const string CommandHelpPrefix = "Command.Help.";
            internal const string CommandHelpSkin = "Command.Help.Skin";
            internal const string CommandHelpSkinAvailability = "Command.Help.SkinAvailability";
            internal const string CommandSettings = "Command.Settings";
            internal const string CommandSettingsTimer = "Command.Settings.Timer";
            internal const string CommandSettingsGrade = "Command.Settings.Grade";
            internal const string WordsDisabled = "Words.Disabled";
            internal const string NoticeDisabledAuto = "Notice.Disabled.Auto";
            internal const string ErrorInvalidArgs = "Error.InvalidArgs";

            internal const string ZeroGrade = "0";
            internal const string FirstGrade = "1";
            internal const string SecondGrade = "2";
            internal const string ThirdGrade = "3";
            internal const string FourthGrade = "4";

            internal const string Adobe = "Adobe";
            internal const string Brick = "Brick";
            internal const string Brutalist = "Brutalist";
            internal const string LegacyWood = "LegacyWood";
            internal const string ShippingContainer = "ShippingContainer";
        }
    }
}

namespace Oxide.Plugins.BGradeExt
{
    public static class BGradeExtensions
    {
        private static readonly Permission permission = Interface.Oxide.GetLibrary<Permission>();
        private static readonly Lang lang = Interface.Oxide.GetLibrary<Lang>();

        public static bool HasAnyPermission(this BasePlayer player, ListHashSet<string> perms)
        {
            foreach (var perm in perms)
            {
                if (!player.HasPermission(perm))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        public static bool HasPermission(this BasePlayer player, string perm)
        {
            return permission.UserHasPermission(player.UserIDString, perm);
        }

        public static bool HasPluginPerm(this BasePlayer player, string perm)
        {
            return permission.UserHasPermission(player.UserIDString, BGrade.Instance.Name.ToLower() + "." + perm);
        }

        public static string Lang(this string key, string id = null, params object[] args)
        {
            return string.Format(lang.GetMessage(key, BGrade.Instance, id), args);
        }

        public static bool HasItemAmount(this BasePlayer player, int itemId, int itemAmount)
        {
            var count = 0;
            foreach (var item in player.inventory.AllItems())
            {
                if (item.info.itemid == itemId)
                {
                    count += item.amount;
                }
            }

            return count >= itemAmount;
        }

        public static bool HasItemAmount(this BasePlayer player, int itemId, int itemAmount, out int amountGot)
        {
            var count = 0;
            foreach (var item in player.inventory.AllItems())
            {
                if (item.info.itemid == itemId)
                {
                    count += item.amount;
                }
            }

            amountGot = count;
            return count >= itemAmount;
        }

        public static void TakeItem(this BasePlayer player, int itemId, int itemAmount)
        {
            if (player.inventory.Take(null, itemId, itemAmount) > 0)
            {
                player.SendConsoleCommand("note.inv", itemId, itemAmount * -1);
            }
        }
    }
}