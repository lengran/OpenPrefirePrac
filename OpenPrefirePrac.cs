using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;
using System.Globalization;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Core.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Entities;

namespace OpenPrefirePrac;

public class OpenPrefirePrac : BasePlugin
{
    public override string ModuleName => "Open Prefire Prac";
    public override string ModuleVersion => "0.1.41";
    public override string ModuleAuthor => "Lengran";
    public override string ModuleDescription => "A plugin for practicing prefire in CS2. https://github.com/lengran/OpenPrefirePrac";

    private readonly Dictionary<int, PlayerStatus> _playerStatuses = new();         // Key: slot of player
    
    private readonly Dictionary<int, int> _ownerOfBots = new();       // Map: slot of bot -> owner.
    
    private readonly Dictionary<string, int> _practiceNameToId = new();
    
    private readonly Dictionary<int, int> _pracConflictCount = new();           // Num of on-going practices that might interfere
    
    private string _mapName = "";
    
    private int _playerCount;
    
    private readonly List<PrefirePractice> _practices = new();
    
    private readonly List<string> _availableMaps = new();

    private readonly ServerStatus _serverStatus = new();

    private CCSGameRules ?_serverGameRules;
    
    private Translator ?_translator;

    private readonly Dictionary<int, int> _botRequests = new();         // make this thread-safe if necessary. Map: Slot of player -> number of bots needed

    private DefaultConfig ?_defaultPlayerSettings;

    private CommandDefinition ?_command;

    private CounterStrikeSharp.API.Modules.Timers.Timer ?_timerBroadcastProgress;
    
    public override void Load(bool hotReload)
    {
        _playerCount = 0;

        _translator = new Translator(Localizer, ModuleDirectory, CultureInfo.CurrentCulture.Name);
        
        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServerHandler);
        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);
        RegisterListener<Listeners.OnTick>(OnTickHandler);

        _defaultPlayerSettings = new DefaultConfig(ModuleDirectory);
        _defaultPlayerSettings.LoadDefaultSettings();

        if (hotReload)
        {
            // Clear status registers
            _ownerOfBots.Clear();
            _practiceNameToId.Clear();
            _pracConflictCount.Clear();
            _practices.Clear();
            _availableMaps.Clear();
            _mapName = "";
            _playerCount = 0;
            _playerStatuses.Clear();
            _botRequests.Clear();
            
            // Clear saved convars
            _serverStatus.WarmupStatus = false;
            _serverStatus.BoolConvars.Clear();
            _serverStatus.IntConvars.Clear();
            _serverStatus.FloatConvars.Clear();
            _serverStatus.StringConvars.Clear();

            // Setup map
            OnMapStartHandler(Server.MapName);
            
            // Setup players
            var players = Utilities.GetPlayers();
            foreach (var tempPlayer in players)
            {
                if (tempPlayer == null || tempPlayer.IsBot || tempPlayer.IsHLTV)
                {
                    continue;
                }

                OnClientPutInServerHandler(tempPlayer.Slot);    
            }
        }

        RegisterCommand();

        if (_timerBroadcastProgress == null)
        {
            _timerBroadcastProgress = AddTimer(3f, () => PrintProgress(), TimerFlags.REPEAT);
        }

        Console.WriteLine("[OpenPrefirePrac] Plugin has been loaded. If the plugin is neither loaded along with server startup, nor reloaded on the fly, please reload it once to make it fully functional.");
    }

    public override void Unload(bool hotReload)
    {
        UnregisterCommand();

        RemoveListener<Listeners.OnClientPutInServer>(OnClientPutInServerHandler);
        RemoveListener<Listeners.OnMapStart>(OnMapStartHandler);
        RemoveListener<Listeners.OnTick>(OnTickHandler);

        if (hotReload)
        {
            // Clear status registers
            _ownerOfBots.Clear();
            _practiceNameToId.Clear();
            _pracConflictCount.Clear();
            _practices.Clear();
            _availableMaps.Clear();
            _mapName = "";
            _playerCount = 0;
            _playerStatuses.Clear();
            _botRequests.Clear();
            
            // Clear saved convars
            _serverStatus.WarmupStatus = false;
            _serverStatus.BoolConvars.Clear();
            _serverStatus.IntConvars.Clear();
            _serverStatus.FloatConvars.Clear();
            _serverStatus.StringConvars.Clear();
        }

        if (_timerBroadcastProgress != null)
        {
            _timerBroadcastProgress.Kill();
            _timerBroadcastProgress = null;
        }

        Console.WriteLine("[OpenPrefirePrac] Plugin has been unloaded.");
    }

    // TODO: Figure out if we can use the GameEventHandler attribute here instead
    // [GameEventHandler]
    public void OnClientPutInServerHandler(int slot)
    {
        // var player = new CCSPlayerController(NativeAPI.GetEntityFromIndex(slot + 1));
        var player = Utilities.GetPlayerFromSlot(slot);

        if (player == null || !player.IsValid || player.IsHLTV)
        {
            return;
        }

        if (player.IsBot)
        {
            // For bots: If someone is practicing and it's an unmanaged bot, add or kick the bot
            if (_playerCount > 0 && !_ownerOfBots.ContainsKey(slot))
            {
                if (_botRequests.Count > 0)
                {
                    // Update requests (move this to the begining of this block)
                    var tmpPlayerNumBots = _botRequests.FirstOrDefault();
                    if (tmpPlayerNumBots.Value == 1)
                    {
                        _botRequests.Remove(tmpPlayerNumBots.Key);
                    }
                    else
                    {
                        _botRequests[tmpPlayerNumBots.Key]--;
                    }

                    // Put this bot under management
                    _playerStatuses[tmpPlayerNumBots.Key].Bots.Add(slot);
                    _ownerOfBots.Add(slot, tmpPlayerNumBots.Key);
                    Console.WriteLine($"[OpenPrefirePrac] Bot {player.PlayerName}, slot: {slot} has been spawned.");
                }
                else
                {
                    // Already have enough bots, kick this bot.
                    KickBot(slot);
                }
            }
        }
        else
        {
            // For players:
            _playerStatuses.Add(slot, new PlayerStatus(_defaultPlayerSettings!));

            // Record player language
            _translator!.RecordPlayerCulture(player);
        }
    }

    [GameEventHandler]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (player == null)
        {
            // Console.WriteLine("[OpenPrefirePrac] OnPlayerDisconnect doesn't work now. Player = null.");
            return HookResult.Continue;
        }

        if (!_playerStatuses.ContainsKey(player.Slot))
            return HookResult.Continue;

        if (_playerStatuses[player.Slot].PracticeIndex != -1)
        {
            ExitPrefireMode(player.Slot);
        }

        // Release resources(practices, targets, bots...)
        _playerStatuses.Remove(player.Slot);
        if (_botRequests.ContainsKey(player.Slot))
        {
            _botRequests.Remove(player.Slot);
        }

        return HookResult.Continue;
    }

    public void OnMapStartHandler(string map)
    {
        _mapName = map;

        // load practices available in current map, from corresponding map directory.
        _availableMaps.Clear();
        var mapDirectories = new List<string>(Directory.EnumerateDirectories(ModuleDirectory + "/maps"));
        var found = false;
        for (var i = 0; i < mapDirectories.Count; i++)
        {
            var mapPath = mapDirectories[i].Substring(mapDirectories[i].LastIndexOf(Path.DirectorySeparatorChar) + 1);
            // Console.WriteLine($"[OpenPrefirePrac] Map folder for map {mapPath} found.");
            _availableMaps.Add(mapPath);

            if (mapPath.Equals(_mapName))
            {
                found = true;
                Console.WriteLine("[OpenPrefirePrac] Map folder for current map found.");
            }
        }

        if (found)
        {
            LoadPractice();
        }
        else
        {
            Console.WriteLine("[OpenPrefirePrac] Failed to load practices on map " + _mapName);
        }
    }

    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var playerOrBot = @event.Userid;
        
        if (playerOrBot == null || !playerOrBot.IsValid|| playerOrBot.IsHLTV)
        {
            // Console.WriteLine($"[OpenPrefirePrac] DEBUG: player is null or hltv.");
            return HookResult.Continue;
        }
        
        if (playerOrBot.IsBot)
        {
            // Console.WriteLine($"[OpenPrefirePrac] DEBUG: A bot {playerOrBot.PlayerName} just spawned.");
            if (_ownerOfBots.ContainsKey(playerOrBot.Slot))
            {
                // Console.WriteLine($"[OpenPrefirePrac] DEBUG: {playerOrBot.PlayerName} is a managed bot.");
                // For managed bots
                int ownerSlot = _ownerOfBots[playerOrBot.Slot];
                var targetNo = _playerStatuses[ownerSlot].Progress;
                var practiceIndex = _playerStatuses[ownerSlot].PracticeIndex;

                if (targetNo < _playerStatuses[ownerSlot].EnabledTargets.Count)
                {
                    // If there are more targets to place, move bot to next place
                    _playerStatuses[ownerSlot].Progress++;
                    // Console.WriteLine($"[OpenPrefirePrac] DEBUG: Update progress to {_playerStatuses[owner].Progress}.");

                    AddTimer(0.5f, () => FreezeBot(playerOrBot));

                    AddTimer(0.55f, () => MovePlayer(playerOrBot,
                        _practices[practiceIndex].Targets[_playerStatuses[ownerSlot].EnabledTargets[targetNo]]
                            .IsCrouching,
                        _practices[practiceIndex].Targets[_playerStatuses[ownerSlot].EnabledTargets[targetNo]].Position,
                        _practices[practiceIndex].Targets[_playerStatuses[ownerSlot].EnabledTargets[targetNo]]
                            .Rotation));
                    

                    // Give bot weapons
                    if (_playerStatuses[ownerSlot].BotWeapon > 0)
                    {
                        SetMoney(playerOrBot, 0);
                        playerOrBot.RemoveWeapons();
                        switch (_playerStatuses[ownerSlot].BotWeapon)
                        {
                            case 1:
                                playerOrBot.GiveNamedItem("weapon_ump45");
                                break;
                            case 2:
                                playerOrBot.GiveNamedItem("weapon_ak47");
                                break;
                            case 3:
                                playerOrBot.GiveNamedItem("weapon_ssg08");
                                break;
                            case 4:
                                playerOrBot.GiveNamedItem("weapon_awp");
                                break;
                            default:
                                playerOrBot.GiveNamedItem("weapon_ak47");
                                break;
                        }
                    }

                    // Try to increase bot difficulty
                    // playerOrBot.beha
                }
                else
                {
                    // This code block is to patch the issue of extra bots.
                    // Explain:
                    //     Bot B is died while Bot A is still spawning, so progress 
                    //     is not updated in time. This could cause Bot B not being
                    //     kicked. So kick them here.
                    _ownerOfBots.Remove(playerOrBot.Slot);
                    _playerStatuses[ownerSlot].Bots.Remove(playerOrBot.Slot);
                    KickBot(playerOrBot.Slot);

                    if (_playerStatuses[ownerSlot].Bots.Count == 0)
                    {
                        // Practice finished.
                        var owner = Utilities.GetPlayerFromSlot(ownerSlot);

                        if (owner != null && owner.IsValid && !owner.IsBot && !owner.IsHLTV)
                        {
                            owner.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator!.Translate(owner, "practice.finish")}");
                            ExitPrefireMode(ownerSlot);
                        }
                    }
                }
            }
            // else if (_playerCount > 0)
            // {
            //     // For unmanaged bots, kick them.
            //     Console.WriteLine($"[OpenPrefirePrac] Find an unmanaged bot ({playerOrBot.PlayerName}) spawning, kick it.");
            //     KickBot(playerOrBot.Slot);
            // }
        }
        else
        {
            // Console.WriteLine($"[OpenPrefirePrac] DEBUG: A player {playerOrBot.PlayerName} just spawned.");
            // For players: Set them up if they are practicing.
            if (!_playerStatuses.ContainsKey(playerOrBot.Slot))
                return HookResult.Continue;

            if (_playerStatuses[playerOrBot.Slot].PracticeIndex < 0)
                return HookResult.Continue;

            // Console.WriteLine($"[OpenPrefirePrac] DEBUG: Setup player {playerOrBot.PlayerName}.");
            SetupPrefireMode(playerOrBot);
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var playerOrBot = @event.Userid;
        
        if (playerOrBot == null || !playerOrBot.IsValid || playerOrBot.IsHLTV)
        {
            return HookResult.Continue;
        }
        
        if (playerOrBot.IsBot) 
        {
            if (_ownerOfBots.ContainsKey(playerOrBot.Slot))
            {
                // For managed bots
                int ownerSlot = _ownerOfBots[playerOrBot.Slot];
                var targetNo = _playerStatuses[ownerSlot].Progress;
                var practiceIndex = _playerStatuses[ownerSlot].PracticeIndex;

                var owner = Utilities.GetPlayerFromSlot(ownerSlot);

                if (owner == null || !owner.IsValid || owner.IsBot || owner.IsHLTV)
                {
                    Console.WriteLine($"[OpenPrefirePrac] The owner of bot ({playerOrBot.PlayerName}) is not right. This could be a bug.");
                    return HookResult.Continue;
                }

                if (targetNo >= _practices[practiceIndex].NumBots)         // Bots might be killed after their first time getting spawned, so as to move them to target spots.
                {
                    // Award the player.
                    if (owner.PawnIsAlive && owner.Pawn.Value != null)
                    {
                        owner.GiveNamedItem("item_assaultsuit");
                        RefillAmmo(owner);

                        if (_playerStatuses[ownerSlot].HealingMethod > 1)
                        {
                            var currentHp = owner.Pawn.Value.Health;
                            switch (_playerStatuses[ownerSlot].HealingMethod)
                            {
                                case 2:
                                    currentHp = currentHp + 25;
                                    break;
                                case 3:
                                    currentHp = 100;
                                    break;
                                case 4:
                                    currentHp = currentHp + 100;
                                    break;
                                case 5:
                                    currentHp = currentHp + 500;
                                    break;
                                default:
                                    currentHp = 100;
                                    break;
                            }
                            SetPlayerHealth(owner, currentHp);
                        }
                    }

                    // Print progress
                    string tmpPractice = _translator!.Translate(owner, "map." + _mapName + "." + _practices[_playerStatuses[ownerSlot].PracticeIndex].PracticeName);
                    string tmpProgress = _translator!.Translate(owner, "practice.progress", _playerStatuses[ownerSlot].EnabledTargets.Count, _playerStatuses[ownerSlot].EnabledTargets.Count - targetNo + _playerStatuses[ownerSlot].Bots.Count - 1);
                    string content = $"{tmpPractice}\u2029{tmpProgress}";
                    owner.PrintToCenter(content);
                }

                // Kick unnecessary bots
                if (targetNo >= _playerStatuses[ownerSlot].EnabledTargets.Count)
                {
                    _ownerOfBots.Remove(playerOrBot.Slot);
                    _playerStatuses[ownerSlot].Bots.Remove(playerOrBot.Slot);
                    KickBot(playerOrBot.Slot);

                    if (_playerStatuses[ownerSlot].Bots.Count == 0)
                    {
                        // Practice finished.
                        owner.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator!.Translate(owner, "practice.finish")}");
                        ExitPrefireMode(ownerSlot);
                    }
                }
                else
                {
                    // Fast respawn
                    AddTimer(0.35f, () => {
                        if (playerOrBot.IsValid && !playerOrBot.PawnIsAlive)
                        {
                            playerOrBot.Respawn();
                        }
                    });
                }
            }
            // else if (_playerCount > 0)
            // {
            //     // For unmanaged bots, kick them.
            //     Console.WriteLine($"[OpenPrefirePrac] Find an unmanaged bot ({playerOrBot.PlayerName}) dying, kick it.");
            //     KickBot(playerOrBot.Slot);
            // }
        }
        else
        {
            // For players: If some bots have already been kicked, add them back.
            if (!_playerStatuses.ContainsKey(playerOrBot.Slot))
                return HookResult.Continue;
            
            var practiceIndex = _playerStatuses[playerOrBot.Slot].PracticeIndex;
            var numBots = _playerStatuses[playerOrBot.Slot].Bots.Count;
            
            if (practiceIndex > -1 && numBots < _practices[practiceIndex].NumBots)
            {
                _playerStatuses[playerOrBot.Slot].Progress = 0;
                AddBot(playerOrBot, _practices[practiceIndex].NumBots - numBots);
            }
        }
        
        return HookResult.Continue;
    }

    public void OnRouteSelect(CCSPlayerController player, ChatMenuOption option)
    {
        int practiceNo = _playerStatuses[player.Slot].LocalizedPracticeNames[option.Text];
        StartPractice(player, practiceNo);
        CloseCurrentMenu(player);
    }

    public void OnForceExitPrefireMode(CCSPlayerController player, ChatMenuOption option)
    {
        ForceStopPractice(player);
        CloseCurrentMenu(player);
    }

    public void OpenMapMenu(CCSPlayerController player, ChatMenuOption option)
    {
        var mapMenu = new ChatMenu(_translator!.Translate(player, "mapmenu.title"));
        foreach (var map in _availableMaps)
        {
            mapMenu.AddMenuOption(map, OnMapSelected);
        }
        mapMenu.AddMenuOption(_translator!.Translate(player, "mainmenu.close_menu"), OnCloseMenu);

        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, mapMenu);
        player.PrintToChat("===========================================");
    }

    public void OnMapSelected(CCSPlayerController player, ChatMenuOption option)
    {
        ChangeMap(player, option.Text);
    }

    public void OnOpenPracticeMenu(CCSPlayerController player, ChatMenuOption option)
    {
        OpenPracticeMenu(player);
    }

    public void OpenDifficultyMenu(CCSPlayerController player, ChatMenuOption option)
    {
        // Dynamically draw menu
        var difficultyMenu = new ChatMenu(_translator!.Translate(player, "difficulty.title"));
        _playerStatuses[player.Slot].LocalizedDifficultyNames.Clear();

        for (var i = 0; i < 5; i++)
        {
            var tmpLocalizedDifficultyName = _translator.Translate(player, $"difficulty.{i}");
            _playerStatuses[player.Slot].LocalizedDifficultyNames.Add(tmpLocalizedDifficultyName, i);
            difficultyMenu.AddMenuOption(tmpLocalizedDifficultyName, OnDifficultyChosen); // practice name here is split by space instead of underline. TODO: Use localized text.
        }
        difficultyMenu.AddMenuOption(_translator!.Translate(player, "mainmenu.close_menu"), OnCloseMenu);

        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, difficultyMenu);
        player.PrintToChat("===========================================");
    }

    public void OnDifficultyChosen(CCSPlayerController player, ChatMenuOption option)
    {
        int difficultyNo = _playerStatuses[player.Slot].LocalizedDifficultyNames[option.Text];
        ChangeDifficulty(player, difficultyNo);
        CloseCurrentMenu(player);
    }

    public void OpenModeMenu(CCSPlayerController player, ChatMenuOption option)
    {
        var trainingModeMenu = new ChatMenu(_translator!.Translate(player, "modemenu.title"));
        _playerStatuses[player.Slot].LocalizedTrainingModeNames.Clear();

        for (var i = 0; i < 2; i++)
        {
            var tmpLocalizedTrainingModeName = _translator.Translate(player, $"modemenu.{i}");
            _playerStatuses[player.Slot].LocalizedTrainingModeNames.Add(tmpLocalizedTrainingModeName, i);
            trainingModeMenu.AddMenuOption(tmpLocalizedTrainingModeName, OnModeChosen);
        }
        trainingModeMenu.AddMenuOption(_translator!.Translate(player, "mainmenu.close_menu"), OnCloseMenu);

        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, trainingModeMenu);
        player.PrintToChat("===========================================");
    }

    public void OnModeChosen(CCSPlayerController player, ChatMenuOption option)
    {
        var trainingModeNo = _playerStatuses[player.Slot].LocalizedTrainingModeNames[option.Text];
        ChangeTrainingMode(player, trainingModeNo);
        CloseCurrentMenu(player);
    }

    public void OpenLanguageMenu(CCSPlayerController player, ChatMenuOption option)
    {
        // No need for localization here.
        var languageMenu = new ChatMenu("Change language settings");

        languageMenu.AddMenuOption("English", OnLanguageChosen);
        languageMenu.AddMenuOption("Português", OnLanguageChosen);
        languageMenu.AddMenuOption("中文", OnLanguageChosen);
        languageMenu.AddMenuOption(_translator!.Translate(player, "mainmenu.close_menu"), OnCloseMenu);

        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, languageMenu);
        player.PrintToChat("===========================================");
    }

    public void OnLanguageChosen(CCSPlayerController player, ChatMenuOption option)
    {
        switch (option.Text)
        {
            case "English":
                _translator!.UpdatePlayerCulture(player.SteamID, "EN");
                break;
            case "Português":
                _translator!.UpdatePlayerCulture(player.SteamID, "pt-BR");
                break;
            case "中文":
                _translator!.UpdatePlayerCulture(player.SteamID, "ZH");
                break;
            default:
                _translator!.UpdatePlayerCulture(player.SteamID, "EN");
                break;
        }

        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator!.Translate(player, "languagemenu.set")}");
        CloseCurrentMenu(player);
    }

    public void OnCloseMenu(CCSPlayerController player, ChatMenuOption option)
    {
        CloseCurrentMenu(player);
    }

    public void OpenBotWeaponMenu(CCSPlayerController player, ChatMenuOption option)
    {
        // Dynamically draw menu
        var botWeaponMenu = new ChatMenu(_translator!.Translate(player, "weaponmenu.title"));

        botWeaponMenu.AddMenuOption(_translator!.Translate(player, "weaponmenu.random"), OnBotWeaponChosen);
        botWeaponMenu.AddMenuOption("UMP-45", OnBotWeaponChosen);
        botWeaponMenu.AddMenuOption("AK47", OnBotWeaponChosen);
        botWeaponMenu.AddMenuOption("SSG08", OnBotWeaponChosen);
        botWeaponMenu.AddMenuOption("AWP", OnBotWeaponChosen);

        botWeaponMenu.AddMenuOption(_translator!.Translate(player, "mainmenu.close_menu"), OnCloseMenu);

        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, botWeaponMenu);
        player.PrintToChat("===========================================");
    }

    public void OnBotWeaponChosen(CCSPlayerController player, ChatMenuOption option)
    {
        int botWeaponChoice = -1;

        switch (option.Text)
        {
            case "UMP-45":
                botWeaponChoice = 1;
                break;
            case "AK47":
                botWeaponChoice = 2;
                break;
            case "SSG08":
                botWeaponChoice = 3;
                break;
            case "AWP":
                botWeaponChoice = 4;
                break;
            default:
                botWeaponChoice = 0;
                break;
        }

        SetBotWeapon(player, botWeaponChoice);

        CloseCurrentMenu(player);
    }

    private void LoadPractice()
    {
        Console.WriteLine($"[OpenPrefirePrac] Loading practices for map {_mapName}.");
        var practiceFiles = new List<string>(Directory.EnumerateFiles($"{ModuleDirectory}/maps/{_mapName}"));
        _practices.Clear();
        _practiceNameToId.Clear();
        _pracConflictCount.Clear();
        for (var i = 0; i < practiceFiles.Count; i++)
        {
            var practiceName = practiceFiles[i].Substring(practiceFiles[i].LastIndexOf(Path.DirectorySeparatorChar) + 1).Split(".")[0];
            _practices.Add(new PrefirePractice(ModuleDirectory, _mapName, practiceName));
            _practiceNameToId.Add(practiceName, i);
            _pracConflictCount.Add(i, 0);
            Console.WriteLine($"[OpenPrefirePrac] {_mapName} {practiceName} Loaded.");
        }
    }
    
    private void ExitPrefireMode(int playerSlot)
    {
        UnsetPrefireMode(playerSlot);

        if (_playerCount == 0)
        {
            RestoreConvars();
        }
    }

    private void ResetBots(int slot)
    {
        _playerStatuses[slot].Progress = 0;

        List<int> botsToDelete = new List<int>();

        // for (var i = 0; i < _playerStatuses[player].Bots.Count; i++)
        foreach (var botSlot in _playerStatuses[slot].Bots)
        {
            // var bot = _playerStatuses[player].Bots[i];
            var bot = Utilities.GetPlayerFromSlot(botSlot);

            if (bot == null || !bot.IsValid)
            {
                Console.WriteLine($"[OpenPrefirePrac] Error: Player has an invalid bot. Unmanage it.");
                botsToDelete.Add(botSlot);
                continue;
            }

            if (bot.PawnIsAlive)
            {
                KillBot(bot);
            }
        }

        AddTimer(3f, () => {
            foreach (int bot in botsToDelete)
            {
                // _playerStatuses[slot].Bots.Remove(bot);
                // _ownerOfBots.Remove(bot);
                KickBot(bot);
            }
        });
    }

    private void SetupPrefireMode(CCSPlayerController player)
    {
        var practiceNo = _playerStatuses[player.Slot].PracticeIndex;
        
        GenerateRandomPractice(player.Slot);
        ResetBots(player.Slot);
        
        AddTimer(0.2f, () => MovePlayer(player, false, _practices[practiceNo].Player.Position, _practices[practiceNo].Player.Rotation));
        AddTimer(0.3f, () => EquipPlayer(player));
        AddTimer(0.35f, () => SetMoney(player, 60000));
        if (_playerStatuses[player.Slot].HealingMethod == 1 || _playerStatuses[player.Slot].HealingMethod == 5)
        {
            AddTimer(0.4f, () => SetPlayerHealth(player, 500));
        }
        else
        {
            AddTimer(0.4f, () => SetPlayerHealth(player, 100));         // in case player got injured by bots before teleport
        }
    }

    private void RemoveBots(int slot)
    {
        List<int> botsToKick = new List<int>();

        foreach (int botSlot in _playerStatuses[slot].Bots)
        {
            var bot = Utilities.GetPlayerFromSlot(botSlot);

            if (bot == null || bot.IsValid)
            {
                botsToKick.Add(botSlot);
            }
            else
            {
                Console.WriteLine($"[OpenPrefirePrac] Trying to kick an invalid bot.");
            }
        }

        foreach (int botSlot in botsToKick)
        {
            KickBot(botSlot);
        }

        _playerStatuses[slot].Progress = 0;
    }

    private void AddBot(CCSPlayerController player, int numberOfBots)
    {
        Console.WriteLine($"[OpenPrefirePrac] Creating {numberOfBots} bots.");

        // Test a new method of adding bots
        if (_botRequests.ContainsKey(player.Slot))
        {
            _botRequests[player.Slot] = numberOfBots;
        }
        else
        {
            _botRequests.Add(player.Slot, numberOfBots);
        }

        for (var i = 0; i < numberOfBots; i++)
        {
            AddTimer(i * 0.1f, () => {
                if (player.TeamNum == (byte)CsTeam.CounterTerrorist)
                {
                    Server.ExecuteCommand("bot_join_team T");
                    Server.ExecuteCommand("bot_add_t");
                }
                else if (player.TeamNum == (byte)CsTeam.Terrorist)
                {
                    Server.ExecuteCommand("bot_join_team CT");
                    Server.ExecuteCommand("bot_add_ct");
                }
            });
        }
    }

    private void MovePlayer(CCSPlayerController? player, bool crouch, Vector pos, QAngle ang)
    {
        if (player == null || !player.IsValid || !player.PawnIsAlive || player.PlayerPawn.Value == null)
        {
            return;
        }

        // Console.WriteLine($"[OpenPrefirePrac] DEBUG: {player.PlayerName} moved to spawn point.");

        // Only bot can crouch
        if (player.IsBot)
        {
            var movementService = new CCSPlayer_MovementServices(player.PlayerPawn.Value.MovementServices!.Handle);
            if (crouch)
            {
                AddTimer(0.05f, () => movementService.DuckAmount = 1);
                AddTimer(0.1f, () => player.PlayerPawn.Value.Bot!.IsCrouching = true);
            }
            else
            {
                AddTimer(0.05f, () => movementService.DuckAmount = 0);
                AddTimer(0.1f, () => player.PlayerPawn.Value.Bot!.IsCrouching = false);
            }
        }
        
        player.PlayerPawn.Value.Teleport(pos, ang, Vector.Zero);
    }

    private void FreezeBot(CCSPlayerController? bot)
    {
        // Console.WriteLine($"[OpenPrefirePrac] DEBUG: Trying to freeze a bot.");
        if (bot != null &&
            bot is { IsValid: true, IsBot: true, IsHLTV: false, PawnIsAlive: true } 
            && bot.PlayerPawn.Value != null
        )
        {
            // Console.WriteLine($"[OpenPrefirePrac] DEBUG: Bot {bot.PlayerName} freezed.");

            // bot.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_OBSOLETE;
            bot.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_VPHYSICS;
            Schema.SetSchemaValue(bot.PlayerPawn.Value.Handle, "CBaseEntity", "m_nActualMoveType", 5);
            Utilities.SetStateChanged(bot.PlayerPawn.Value, "CBaseEntity", "m_MoveType");
        }
    }

    private void EquipPlayer(CCSPlayerController player)
    {
        if (player == null || !player.PawnIsAlive || player.PlayerPawn.Value == null)
            return;
        
        if (_defaultPlayerSettings!.EquipPlayer == 1)
        {
            player.RemoveWeapons();

            // Give weapons and items
            player.GiveNamedItem("weapon_ak47");
            player.GiveNamedItem("weapon_deagle");
            player.GiveNamedItem("weapon_knife");
            player.GiveNamedItem("weapon_flashbang");
            player.GiveNamedItem("weapon_flashbang");
            player.GiveNamedItem("weapon_smokegrenade");
            player.GiveNamedItem("item_assaultsuit");

            // Switch to main weapon
            player.ExecuteClientCommand("slot1");
        }
        else
        {
            player.GiveNamedItem("weapon_knife");
            player.GiveNamedItem("item_assaultsuit");
        }
    }

    private static void SetPlayerHealth(CCSPlayerController player, int hp)
    {
        if (player == null || !player.PawnIsAlive || player.Pawn.Value == null || hp < 0)
            return;
        
        // Console.WriteLine($"[OpenPrefirePrac] DEBUG: Setup player {player.PlayerName} with health.");

        if (hp > 100)
            player.Pawn.Value.MaxHealth = hp;
        player.Pawn.Value.Health = hp;
        Utilities.SetStateChanged(player.Pawn.Value, "CBaseEntity", "m_iHealth");
    }

    private void GenerateRandomPractice(int playerSlot)
    {
        _playerStatuses[playerSlot].EnabledTargets.Clear();
        var practiceNo = _playerStatuses[playerSlot].PracticeIndex;
        
        for (var i = 0; i < _practices[practiceNo].Targets.Count; i++)
            _playerStatuses[playerSlot].EnabledTargets.Add(i);

        if (_playerStatuses[playerSlot].TrainingMode == 0)
        {
            // 0: Use part of the targets.
            var numTargets = (int)(_practices[practiceNo].SpawnRatio * _practices[practiceNo].Targets.Count);
            var rnd = new Random(DateTime.Now.Millisecond);

            var numToRemove = _practices[practiceNo].Targets.Count - numTargets;
            for (var i = 0; i < numToRemove; i++)
                _playerStatuses[playerSlot].EnabledTargets.RemoveAt(rnd.Next(_playerStatuses[playerSlot].EnabledTargets.Count));
        }
        // 1: Use all of the targets.
    }

    private void CreateGuidingLine(int playerSlot)
    {
        var practiceNo = _playerStatuses[playerSlot].PracticeIndex;

        if (practiceNo < 0 || practiceNo >= _practices.Count)
        {
            Console.WriteLine($"[OpenPrefirePrac] Error when creating guiding line. Current practice_no illegal. (practice_no = {practiceNo})");
            return;
        }

        if (_practices[practiceNo].GuidingLine.Count < 2)
            return;

        // Draw beams
        for (int i = 0; i < _practices[practiceNo].GuidingLine.Count - 1; i++)
        {
            int beamIndex = DrawBeam(_practices[practiceNo].GuidingLine[i], _practices[practiceNo].GuidingLine[i + 1]);
            
            if (beamIndex == -1)
                return;

            _playerStatuses[playerSlot].Beams.Add(beamIndex);
        }
    }

    private void DeleteGuidingLine(int playerSlot)
    {
        for (var i = 0; i < _playerStatuses[playerSlot].Beams.Count; i++)
        {
            var beam = Utilities.GetEntityFromIndex<CBeam>(_playerStatuses[playerSlot].Beams[i]);

            if (beam == null || !beam.IsValid)
            {
                Console.WriteLine($"[OpenPrefirePrac] Error when deleting guiding line. Failed to get beam entity(index = {_playerStatuses[playerSlot].Beams[i]})");
                continue;
            }

            beam.Remove();
        }

        _playerStatuses[playerSlot].Beams.Clear();
    }

    private static int DrawBeam(Vector startPos, Vector endPos)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam == null)
        {
            // Failed to create beam
            Console.WriteLine($"[OpenPrefirePrac] Failed to create beam. Start position: {startPos}, end position: {endPos}");
            return -1;
        }

        beam.Render = System.Drawing.Color.Blue;
        beam.Width = 2.0f;

        beam.Teleport(startPos, QAngle.Zero, Vector.Zero);
        beam.EndPos.Add(endPos);
        beam.DispatchSpawn();

        // Console.WriteLine($"[OpenPrefirePrac] Created a beam. Start position: {startPos}, end position: {endPos}, entity index: {beam.Index}");
        return (int)beam.Index;
    }

    private void SaveConvars()
    {
        string[] boolConvarNames = [
            "tv_enable",
            "bot_allow_grenades",
            "bot_allow_shotguns",
            "mp_autoteambalance",
            "sv_alltalk",
            "sv_full_alltalk",
            "bot_allow_pistols",
            "bot_allow_rifles",
            "bot_allow_snipers",
            "sv_auto_adjust_bot_difficulty",
        ];

        string[] intConvarNames = [
            "mp_buy_anywhere",
            "mp_warmup_pausetimer",
            "mp_free_armor",
            "mp_limitteams",
            // "sv_infinite_ammo",
            "mp_maxmoney",
            "mp_startmoney",
            "bot_difficulty",
            "custom_bot_difficulty",
            "mp_death_drop_gun",
            "mp_death_drop_grenade",
            "bot_quota",
        ];

        string[] floatConvarNames = [
            "mp_respawn_immunitytime",
            "mp_buytime",
            "bot_max_vision_distance_override",
        ];

        string[] stringConvarNames = [
            "bot_quota_mode",
            "mp_ct_default_melee",
            "mp_t_default_melee",
            "mp_bot_ai_bt",
        ];

        try
        {
            // // sv_cheats
            // var sv_cheats = ConVar.Find("sv_cheats");
            // _serverStatus.sv_cheats = sv_cheats!.GetPrimitiveValue<bool>();

            // Bool convars
            foreach (var convarName in boolConvarNames)
            {
                var tmpConvar = ConVar.Find(convarName);
                if (tmpConvar != null)
                {
                    var value = tmpConvar.GetPrimitiveValue<bool>();
                    _serverStatus.BoolConvars.Add(convarName, value);
                    // Console.WriteLine($"[OpenPrefirePrac] {convarName}: {value}");
                }
            }

            // Int convars
            foreach (var convarName in intConvarNames)
            {
                var tmpConvar = ConVar.Find(convarName);
                if (tmpConvar != null)
                {
                    var value = tmpConvar.GetPrimitiveValue<int>();
                    _serverStatus.IntConvars.Add(convarName, value);
                    // Console.WriteLine($"[OpenPrefirePrac] {convarName}: {value}");
                }
            }

            // Float convars
            foreach (var convarName in floatConvarNames)
            {
                var tmpConvar = ConVar.Find(convarName);
                if (tmpConvar != null)
                {
                    var value = tmpConvar.GetPrimitiveValue<float>();
                    _serverStatus.FloatConvars.Add(convarName, value);
                    // Console.WriteLine($"[OpenPrefirePrac] {convarName}: {value}");
                }
            }

            // String convars
            foreach (var convarName in stringConvarNames)
            {
                var tmpConvar = ConVar.Find(convarName);
                if (tmpConvar != null)
                {
                    var value = tmpConvar.StringValue;
                    _serverStatus.StringConvars.Add(convarName, value);
                    // Console.WriteLine($"[OpenPrefirePrac] DEBUG {convarName}: {value}");
                }
            }
        }
        catch (System.Exception)
        {
            Console.WriteLine("[OpenPrefirePrac] Error reading convars.");
            throw;
        }

        // Read Warmup status
        try
        {
            if (_serverGameRules == null)
            {
                _serverGameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
            }
            _serverStatus.WarmupStatus = _serverGameRules.WarmupPeriod;
            // Console.WriteLine($"[OpenPrefirePrac] Warmup Status: {_serverGameRules.WarmupPeriod}");
        }
        catch (System.Exception)
        {
            Console.WriteLine($"[OpenPrefirePrac] Can't read server's warmup status, will use the default value {_serverStatus.WarmupStatus}.");
        }

        Console.WriteLine("[OpenPrefirePrac] Values of convars saved.");
    }

    private void RestoreConvars()
    {
        // Bool convars
        foreach (var convar in _serverStatus.BoolConvars)
        {
            // var tmpConvar = ConVar.Find(convar.Key);
            // tmpConvar!.SetValue(convar.Value);
            Server.ExecuteCommand(convar.Key + " " + convar.Value.ToString());
        }
        _serverStatus.BoolConvars.Clear();

        // Int convars
        foreach (var convar in _serverStatus.IntConvars)
        {
            // var tmpConvar = ConVar.Find(convar.Key);
            // Somehow the following 2 methods don't work, just make up a command to implement this.
            Server.ExecuteCommand(convar.Key + " " + convar.Value.ToString());
            // tmpConvar!.GetPrimitiveValue<int>() = convar.Value;
            // tmpConvar!.SetValue(convar.Value);
        }
        _serverStatus.IntConvars.Clear();

        // Float convars
        foreach (var convar in _serverStatus.FloatConvars)
        {
            // var tmpConvar = ConVar.Find(convar.Key);
            // Somehow the following 2 methods don't work, just make up a command to implement this.
            Server.ExecuteCommand(convar.Key + " " + convar.Value.ToString());
            // tmpConvar!.GetPrimitiveValue<float>() = convar.Value;
            // tmpConvar!.SetValue(convar.Value);
        }
        _serverStatus.FloatConvars.Clear();

        // String convars
        foreach (var convar in _serverStatus.StringConvars)
        {
            // var tmpConvar = ConVar.Find(convar.Key);
            // tmpConvar!.StringValue = convar.Value;
            Server.ExecuteCommand(convar.Key + " \"" + convar.Value + "\"");
        }
        _serverStatus.StringConvars.Clear();

        // Restore sv_cheats
        // var sv_cheats = ConVar.Find("sv_cheats");
        // sv_cheats!.SetValue(_serverStatus.sv_cheats);
        // Server.ExecuteCommand("sv_cheats " + _serverStatus.sv_cheats.ToString());

        // Restore warmup status
        if (!_serverStatus.WarmupStatus)
        {
            Server.ExecuteCommand("mp_warmup_end");
        }

        Console.WriteLine("[OpenPrefirePrac] Values of convars restored.");
    }

    private void SetupConvars()
    {
        Server.ExecuteCommand("tv_enable 0");
        // Server.ExecuteCommand("sv_cheats 1");
        Server.ExecuteCommand("bot_allow_grenades 0");
        Server.ExecuteCommand("bot_allow_shotguns 0");
        Server.ExecuteCommand("mp_autoteambalance 0");
        Server.ExecuteCommand("sv_alltalk 1");
        Server.ExecuteCommand("sv_full_alltalk 1");
        Server.ExecuteCommand("bot_allow_pistols 1");
        Server.ExecuteCommand("bot_allow_rifles 1");
        Server.ExecuteCommand("bot_allow_snipers 1");
        Server.ExecuteCommand("sv_auto_adjust_bot_difficulty 0");

        Server.ExecuteCommand("mp_buy_anywhere 1");
        Server.ExecuteCommand("mp_warmup_pausetimer 1");
        Server.ExecuteCommand("mp_free_armor 2");
        Server.ExecuteCommand("mp_limitteams 0");
        // Server.ExecuteCommand("sv_infinite_ammo 1");
        Server.ExecuteCommand("mp_maxmoney 60000");
        Server.ExecuteCommand("mp_startmoney 60000");
        Server.ExecuteCommand("bot_difficulty 5");
        Server.ExecuteCommand("custom_bot_difficulty 5");
        Server.ExecuteCommand("mp_death_drop_gun 0");
        Server.ExecuteCommand("mp_death_drop_grenade 0");
        Server.ExecuteCommand("bot_quota 0");

        Server.ExecuteCommand("mp_respawn_immunitytime -1");
        Server.ExecuteCommand("mp_buytime 9999");
        Server.ExecuteCommand("bot_max_vision_distance_override 99999");

        Server.ExecuteCommand("bot_quota_mode normal");
        Server.ExecuteCommand("mp_ct_default_melee \"\"");
        Server.ExecuteCommand("mp_t_default_melee \"\"");
        
        Server.ExecuteCommand("mp_warmup_start");
        // Server.ExecuteCommand("bot_kick all");

        // Set behavior tree for bots (many thanks to 5EPlay)
        if (_defaultPlayerSettings!.BotAimLock == 2)
        {
            Server.ExecuteCommand("mp_bot_ai_bt \"addons\\counterstrikesharp\\plugins\\OpenPrefirePrac\\resources\\bt\\hard_mode.kv3\"");
            Server.ExecuteCommand("mp_bot_ai_bt_clear_cache");
        }

        // Kick unmanaged bots
        AddTimer(2f, () => {
            var players = Utilities.GetPlayers();
            List<CCSPlayerController> botsToKick = new List<CCSPlayerController>();
            foreach(var player in players)
            {
                if (player == null || !player.IsValid || player.IsHLTV || !player.IsBot)
                {
                    continue;
                }

                if (_ownerOfBots.ContainsKey(player.Slot))
                {
                    continue;
                }

                botsToKick.Add(player);
            }
            foreach (var bot in botsToKick)
            {
                KickBot(bot.Slot);
            }
            botsToKick.Clear();
        });

        // Server.ExecuteCommand("bot_autodifficulty_threshold_high 5");
        // Server.ExecuteCommand("bot_autodifficulty_threshold_low 5");
        // Server.ExecuteCommand("sv_auto_adjust_bot_difficulty 0");
        // Server.ExecuteCommand("weapon_auto_cleanup_time 1");       
        // Server.ExecuteCommand("mp_roundtime 60");
        // Server.ExecuteCommand("mp_roundtime_defuse 60");
        // Server.ExecuteCommand("mp_freezetime 0");
        // Server.ExecuteCommand("mp_team_intro_time 0");
        // Server.ExecuteCommand("mp_ignore_round_win_conditions 1");
        // Server.ExecuteCommand("mp_respawn_on_death_ct 1");
        // Server.ExecuteCommand("mp_respawn_on_death_t 1");

        Console.WriteLine("[OpenPrefirePrac] Values of convars set.");
    }

    private void RefillAmmo(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || !player.PawnIsAlive || player.PlayerPawn == null || player.PlayerPawn.Value == null || player.PlayerPawn.Value.WeaponServices == null)
        {
            return;
        }

        var weapons = player.PlayerPawn.Value.WeaponServices.MyWeapons;
        foreach (var weapon in weapons)
        {
            if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.DesignerName.Length != 0 && !weapon.Value.DesignerName.Contains("knife") && !weapon.Value.DesignerName.Contains("bayonet"))
            {
                int magAmmo = 999;
                int reservedAmmo = 999;
                switch (weapon.Value.DesignerName)
                {
                    case "weapon_ak47":
                    case "weapon_m4a1":         // M4A4
                        magAmmo = 31;
                        reservedAmmo = 90;
                        break;
                    case "weapon_m4a1_":         // M4A1_silencer
                        magAmmo = 21;
                        reservedAmmo = 80;
                        break;
                    case "weapon_deagle":
                        magAmmo = 8;
                        reservedAmmo = 35;
                        break;
                    case "weapon_flashbang":
                    case "weapon_smokegrenade":
                    case "weapon_decoy":
                    case "weapon_molotov":
                    case "weapon_incgrenade":
                        continue;
                    default:
                        magAmmo = 999;
                        reservedAmmo = 999;
                        break;
                }

                weapon.Value.Clip1 = magAmmo;
                Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_iClip1");
                weapon.Value.ReserveAmmo[0] = reservedAmmo;
                Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_pReserveAmmo");
            }
        }
    }

    private void KillBot(CCSPlayerController bot)
    {
        if (!bot.IsValid || !bot.IsBot || bot.IsHLTV || !bot.PawnIsAlive)
        {
            return;
        }

        bot.CommitSuicide(false, false);
    }

    private void StartPractice(CCSPlayerController player, int practiceIndex)
    {
        if (_playerCount == 0)
        {
            SaveConvars();
            SetupConvars();
            AddTimer(0.5f, () => BreakBreakables());
        }

        var previousPracticeIndex = _playerStatuses[player.Slot].PracticeIndex;

        if (previousPracticeIndex != practiceIndex)
        {
            // Check if selected practice route is compatible with other on-playing routes.
            if (_pracConflictCount[practiceIndex] > 0)
            {
                player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator!.Translate(player, "practice.incompatible")}");
                return;
            }

            // Update practice status
            if (previousPracticeIndex > -1)
            {
                UnsetPrefireMode(player.Slot);
                DeleteGuidingLine(player.Slot);
            }
            
            _playerCount++;
            _playerStatuses[player.Slot].PracticeIndex = practiceIndex;
            AddTimer(1f, () => CreateGuidingLine(player.Slot));

            // Disable incompatible practices.
            for (var i = 0; i < _practices[practiceIndex].IncompatiblePractices.Count; i++)
            {
                if (_practiceNameToId.ContainsKey(_practices[practiceIndex].IncompatiblePractices[i]))
                {
                    var disabledPracticeNo = _practiceNameToId[_practices[practiceIndex].IncompatiblePractices[i]];
                    _pracConflictCount[disabledPracticeNo]++;
                }
            }
            _pracConflictCount[practiceIndex]++;

            // Setup practice
            AddBot(player, _practices[practiceIndex].NumBots);
        }
        else
        {
            // If some bots have already been kicked, add them back.
            var numRemainingBots = _playerStatuses[player.Slot].Bots.Count;
            
            if (numRemainingBots < _practices[practiceIndex].NumBots)
            {
                _playerStatuses[player.Slot].Progress = 0;
                AddBot(player, _practices[practiceIndex].NumBots - numRemainingBots);
            }
        }

        // Practice begin
        SetupPrefireMode(player);
        var localizedPracticeName = _translator!.Translate(player, "map." + _mapName + "." + _practices[practiceIndex].PracticeName);
        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator.Translate(player, "practice.choose", localizedPracticeName)}");
        player.PrintToCenter(_translator.Translate(player, "practice.begin"));
    }

    private void ChangeMap(CCSPlayerController player, string mapName)
    {
        // Check if the map has practice routes
        if (!_availableMaps.Contains(mapName))
        {
            player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator!.Translate(player, "mapmenu.not_available")}");
            return;
        }

        // Only allow change map when nobody is practicing.
        if (_playerCount == 0)
        {
            Server.ExecuteCommand($"changelevel {mapName}");
        }
        else
        {
            player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator!.Translate(player, "mapmenu.busy")}");
        }
    }

    private void ChangeDifficulty(CCSPlayerController player, int difficultyNo)
    {
        _playerStatuses[player.Slot].HealingMethod = difficultyNo;
        var currentDifficulty = _translator!.Translate(player, $"difficulty.{difficultyNo}");
        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator!.Translate(player, "difficulty.set", currentDifficulty)}");
    }

    private void ChangeTrainingMode(CCSPlayerController player, int trainingMode)
    {
        _playerStatuses[player.Slot].TrainingMode = trainingMode;
        var currentTrainingMode = _translator!.Translate(player, $"modemenu.{trainingMode}");
        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator!.Translate(player, "modemenu.set", currentTrainingMode)}");
    }

    private void ForceStopPractice(CCSPlayerController player)
    {
        ExitPrefireMode(player.Slot);
        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator!.Translate(player, "practice.exit")}");
    }

    private void UnregisterCommand()
    {
        if (_command == null)
        {
            return;
        }

        CommandManager.RemoveCommand(_command);
        _command = null;
    }

    private void RegisterCommand()
    {
        if (_command != null)
        {
            UnregisterCommand();
        }

        _command = new CommandDefinition("css_prefire", "Command to bring up the main menu of OpenPrefirePrac.", (player, commandInfo) => {
            // This is a client only command
            if (player == null)
            {
                return;
            }

            // Command shortcuts
            if (commandInfo.ArgCount > 1)
            {
                switch (commandInfo.ArgByIndex(1))
                {
                    case "prac":
                        int choice = 0;
                        if (int.TryParse(commandInfo.ArgByIndex(2), out choice) && choice > 0 && choice <= _practices.Count)
                        {
                            StartPractice(player, choice - 1);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator!.Translate(player, "practice.help", _practices.Count)}");
                        return;
                    case "map":
                        string mapName = commandInfo.ArgByIndex(2);
                        ChangeMap(player, mapName);
                        return;
                    case "df":
                        int difficulty = 0;
                        if (int.TryParse(commandInfo.ArgByIndex(2), out difficulty) && difficulty > 0 && difficulty <= 6)
                        {
                            ChangeDifficulty(player, 6 - difficulty);
                            return;
                        }
                        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator!.Translate(player, "difficulty.help")}");
                        return;
                    case "mode":
                        string trainingMode = commandInfo.ArgByIndex(2);
                        switch (trainingMode)
                        {
                            case "full":
                                ChangeTrainingMode(player, 1);
                                return;
                            case "rand":
                                ChangeTrainingMode(player, 0);
                                return;
                            default:
                                player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator!.Translate(player, "modemenu.help")}");
                                return;
                        }
                    case "bw":
                        string botWeapon = commandInfo.ArgByIndex(2);
                        switch (botWeapon)
                        {
                            case "rand":
                                SetBotWeapon(player, 0);
                                return;
                            case "ump":
                                SetBotWeapon(player, 1);
                                return;
                            case "ak":
                                SetBotWeapon(player, 2);
                                return;
                            case "sct":
                                SetBotWeapon(player, 3);
                                return;
                            case "awp":
                                SetBotWeapon(player, 4);
                                return;
                            default:
                                player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator!.Translate(player, "weaponmenu.help")}");
                                return;
                        }
                    case "lang":
                        string language = commandInfo.ArgByIndex(2);
                        switch (language)
                        {
                            case "en":
                                _translator!.UpdatePlayerCulture(player.SteamID, "EN");
                                break;
                            case "pt":
                                _translator!.UpdatePlayerCulture(player.SteamID, "pt-BR");
                                break;
                            case "zh":
                                _translator!.UpdatePlayerCulture(player.SteamID, "ZH");
                                break;
                            default:
                                player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator!.Translate(player, "languagemenu.help")}");
                                return;
                        }
                        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator!.Translate(player, "languagemenu.set")}");
                        return;
                    case "exit":
                        ForceStopPractice(player);
                        return;
                    case "help":
                        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator!.Translate(player, "mainmenu.help", _practices.Count)}");
                        return;
                    // case "test":
                    //     SetMoney(player, 0);
                    //     AddTimer(5f, () => SetMoney(player, 123));
                    //     return;
                    default:
                        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator!.Translate(player, "mainmenu.help", _practices.Count)}");
                        break;
                }
            }

            // Draw the menu
            var mainMenu = new ChatMenu(_translator!.Translate(player, "mainmenu.title"));

            // 1 Practice menu
            mainMenu.AddMenuOption(_translator.Translate(player, "mainmenu.practice"), OnOpenPracticeMenu);

            // 2 Map menu
            mainMenu.AddMenuOption(_translator.Translate(player, "mainmenu.map"), OpenMapMenu);

            // 3 Difficulty menu
            string currentDifficulty = _translator.Translate(player, $"difficulty.{_playerStatuses[player.Slot].HealingMethod}");
            mainMenu.AddMenuOption(_translator.Translate(player, "mainmenu.difficulty", currentDifficulty), OpenDifficultyMenu);
            
            // 4 Training mode menu
            string currentTrainingMode = _translator.Translate(player, $"modemenu.{_playerStatuses[player.Slot].TrainingMode}");
            mainMenu.AddMenuOption(_translator.Translate(player, "mainmenu.mode", currentTrainingMode), OpenModeMenu);

            // 5 Bot weapon menu
            string currentBotWeapon = "";
            switch (_playerStatuses[player.Slot].BotWeapon)
            {
                case 0:
                    currentBotWeapon = _translator!.Translate(player, "weaponmenu.random");
                    break;
                case 1:
                    currentBotWeapon = "UMP-45";
                    break;
                case 2:
                    currentBotWeapon = "AK47";
                    break;
                case 3:
                    currentBotWeapon = "AWP";
                    break;
                default:
                    break;
            }
            mainMenu.AddMenuOption(_translator!.Translate(player, "mainmenu.bot_weapon", currentBotWeapon), OpenBotWeaponMenu);

            // 6 Language menu
            mainMenu.AddMenuOption("Language preference", OpenLanguageMenu);

            // 7 (Optional) End practicing button
            if (_playerStatuses[player.Slot].PracticeIndex > 0)
            {
                mainMenu.AddMenuOption(_translator.Translate(player, "mainmenu.exit"), OnForceExitPrefireMode);
            }

            // 8 Close menu button.
            mainMenu.AddMenuOption(_translator!.Translate(player, "mainmenu.close_menu"), OnCloseMenu);

            player.PrintToChat("============ [OpenPrefirePrac] ============");
            MenuManager.OpenChatMenu(player, mainMenu);
            player.PrintToChat(_translator.Translate(player, "mainmenu.shortcut_prompt"));
            player.PrintToChat("===========================================");
        });

        CommandManager.RegisterCommand(_command);
    }

    private void CloseCurrentMenu(CCSPlayerController player)
    {
        MenuManager.CloseActiveMenu(player);
        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator!.Translate(player, "mainmenu.menu_closed")}");
    }

    private void SetBotWeapon(CCSPlayerController player, int botWeapon)
    {
        _playerStatuses[player.Slot].BotWeapon = botWeapon;

        string weaponName = "";
        switch (botWeapon)
        {
            case 0:
                weaponName = _translator!.Translate(player, "weaponmenu.random");
                break;
            case 1:
                weaponName = "UMP-45";
                break;
            case 2:
                weaponName = "AK47";
                break;
            case 3:
                weaponName = "SSG08";
                break;
            case 4:
                weaponName = "AWP";
                break;
        }

        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator!.Translate(player, "weaponmenu.set", weaponName)}");
    }

    private void OpenPracticeMenu(CCSPlayerController player)
    {
        // Dynamically draw menu
        var practiceMenu = new ChatMenu(_translator!.Translate(player, "practicemenu.title"));
        _playerStatuses[player.Slot].LocalizedPracticeNames.Clear();

        // Add menu options for practices
        for (var i = 0; i < _practices.Count; i++)
        {
            if (_pracConflictCount[i] == 0)
            {
                var tmpLocalizedPracticeName = _translator.Translate(player, $"map.{_mapName}.{_practices[i].PracticeName}");
                _playerStatuses[player.Slot].LocalizedPracticeNames.Add(tmpLocalizedPracticeName, i);
                practiceMenu.AddMenuOption(tmpLocalizedPracticeName, OnRouteSelect); // practice name here is split by space instead of underline. TODO: Use localized text.
            }
        }
        int practiceNo = _playerStatuses[player.Slot].PracticeIndex;
        if (practiceNo > -1)
        {
            var tmpLocalizedPracticeName = _translator.Translate(player, $"map.{_mapName}.{_practices[practiceNo].PracticeName}");
            _playerStatuses[player.Slot].LocalizedPracticeNames.Add(tmpLocalizedPracticeName, practiceNo);
            practiceMenu.AddMenuOption(tmpLocalizedPracticeName, OnRouteSelect);
        }

        practiceMenu.AddMenuOption(_translator!.Translate(player, "mainmenu.close_menu"), OnCloseMenu);

        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, practiceMenu);
        player.PrintToChat("===========================================");
    }

    private void UnsetPrefireMode(int playerSlot)
    {
        var previousPracticeNo = _playerStatuses[playerSlot].PracticeIndex;
        if (previousPracticeNo > -1)
        {
            RemoveBots(playerSlot);
            DeleteGuidingLine(playerSlot);

            // Enable disabled practice routes
            for (var i = 0; i < _practices[previousPracticeNo].IncompatiblePractices.Count; i++)
            {
                if (_practiceNameToId.TryGetValue(_practices[previousPracticeNo].IncompatiblePractices[i], out var value))
                {
                    _pracConflictCount[value]--;
                }
            }
            _pracConflictCount[previousPracticeNo]--;

            _playerStatuses[playerSlot].PracticeIndex = -1;
            _playerCount--;

            // patch: check and remove request of bots in case something goes wrong resulting in a stuck request
            if (_botRequests.ContainsKey(playerSlot))
            {
                _botRequests.Remove(playerSlot);
            }
        }
    }

    private void SetMoney(CCSPlayerController player, int money)
    {
        var moneyServices = player.InGameMoneyServices;
        if (moneyServices == null)
        {
            return;
        }
        
        moneyServices.Account = money;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
    }

    private void PrintProgress()
    {
        var players = Utilities.GetPlayers();

        foreach (var player in players)
        {
            if (!player.IsValid || player.IsBot || player.IsHLTV || !_playerStatuses.ContainsKey(player.Slot) || _playerStatuses[player.Slot].PracticeIndex < 0)
            {
                continue;
            }

            // If player is practicing, print timer
            string tmpPractice = _translator!.Translate(player, "map." + _mapName + "." + _practices[_playerStatuses[player.Slot].PracticeIndex].PracticeName);
            string tmpProgress = _translator!.Translate(player, "practice.progress", _playerStatuses[player.Slot].EnabledTargets.Count, _playerStatuses[player.Slot].EnabledTargets.Count - _playerStatuses[player.Slot].Progress + _playerStatuses[player.Slot].Bots.Count);
            string content = $"{tmpPractice}\u2029{tmpProgress}";

            player.PrintToCenter(content);
        }
    }

    private void KickBot(int botSlot)
    {

        if (_ownerOfBots.ContainsKey(botSlot))
        {
            int ownerSlot = _ownerOfBots[botSlot];
            if (_playerStatuses[ownerSlot].Bots.Contains(botSlot))
            {
                _playerStatuses[ownerSlot].Bots.Remove(botSlot);
            }
            _ownerOfBots.Remove(botSlot);
        }

        var bot = Utilities.GetPlayerFromSlot(botSlot);

        if (bot == null || !bot.IsValid || !bot.IsBot)
        {
            return;
        }

        Server.ExecuteCommand($"bot_kick {bot.PlayerName}");
        Console.WriteLine($"[OpenPrefirePrac] Exec command: bot_kick {bot.PlayerName}");
    }

    // Thanks to B3none
    // Code borrowed from cs2-retake/RetakesPlugin/Modules/Managers/BreakerManager.cs
    private void BreakBreakables()
    {
        // Enable this feature only on nuke and mirage. (mirage is disabled because of the crash issue on Windows)
        if (Server.MapName != "de_nuke") // && Server.MapName != "de_mirage")
        {
            Console.WriteLine($"[OpenPrefirePrac] Map {Server.MapName} doesn't have breakables to break.");
            return;
        }

        Console.WriteLine($"[OpenPrefirePrac] Map {Server.MapName} have breakables to break.");

        // Enable certain breakables on certain maps to avoid game crash
        List<string> enabled_breakables =
        [
            // Common breakables
            "func_breakable",
            "func_breakable_surf",
            "prop.breakable.01",
            "prop.breakable.02",
        ];

        if (Server.MapName == "de_nuke")
        {
            enabled_breakables.Add("prop_door_rotating");
            enabled_breakables.Add("prop_dynamic");
        }

        if (Server.MapName == "de_mirage")
        {
            enabled_breakables.Add("prop_dynamic");
        }

        // Console.WriteLine($"[OpenPrefirePrac] DEBUG: Have breakables: {enabled_breakables}");

        // Loop to find breakables
        CEntityIdentity ?pEntity = new CEntityIdentity(EntitySystem.FirstActiveEntity);
        while (pEntity != null && pEntity.Handle != IntPtr.Zero)
        {
            if (!enabled_breakables.Contains(pEntity.DesignerName))
            {
                pEntity = pEntity.Next;
                continue;
            }

            switch (pEntity.DesignerName)
            {
                case "func_breakable":
                case "func_breakable_surf":
                case "prop.breakable.01":
                case "prop.breakable.02":
                case "prop_dynamic":
                    CBreakable breakableEntity = new PointerTo<CBreakable>(pEntity.Handle).Value;
                    if (breakableEntity.IsValid)
                    {
                        breakableEntity.AcceptInput("Break");
                    }
                    break;
                case "func_button":
                    CBaseButton button = new PointerTo<CBaseButton>(pEntity.Handle).Value;
                    if (button.IsValid)
                    {
                        button.AcceptInput("Kill");
                    }
                    break;
                case "prop_door_rotating":
                    CPropDoorRotating propDoorRotating = new PointerTo<CPropDoorRotating>(pEntity.Handle).Value;
                    if (propDoorRotating.IsValid)
                    {
                        propDoorRotating.AcceptInput("Open");
                    }
                    break;
                default:
                    break;
            }

            // Get next entity
            pEntity = pEntity.Next;
        }
    }

    private void OnTickHandler()
    {
        if (_defaultPlayerSettings!.BotAimLock == 1)
        {
            foreach (int playerSlot in _playerStatuses.Keys)
            {
                var player = Utilities.GetPlayerFromSlot(playerSlot);

                if (player == null || !player.IsValid || !player.PawnIsAlive || player.PlayerPawn.Value == null)
                {
                    continue;
                }

                // Aimlock bots
                Vector ownerEyePos = new Vector(player.PlayerPawn.Value.AbsOrigin!.X, player.PlayerPawn.Value.AbsOrigin!.Y, player.PlayerPawn.Value.AbsOrigin!.Z + player.PlayerPawn.Value.ViewmodelOffsetZ);

                foreach(var botSlot in _playerStatuses[playerSlot].Bots)
                {
                    var bot = Utilities.GetPlayerFromSlot(botSlot);

                    if (bot == null || !bot.IsValid || !bot.PawnIsAlive || bot.PlayerPawn.Value == null)
                    {
                        continue;
                    }

                    Vector botEyePosition = new Vector(bot.PlayerPawn.Value.AbsOrigin!.X, bot.PlayerPawn.Value.AbsOrigin!.Y, bot.PlayerPawn.Value.AbsOrigin!.Z + bot.PlayerPawn.Value.ViewmodelOffsetZ);

                    // calculate angle
                    float deltaX = ownerEyePos.X - botEyePosition.X;
                    float deltaY = ownerEyePos.Y - botEyePosition.Y;
                    float deltaZ = ownerEyePos.Z - botEyePosition.Z;
                    double yaw = 180 * Math.Atan2(deltaY, deltaX) / Math.PI;
                    double tmp = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                    double pitch= 180 * Math.Atan2(-1 * deltaZ, tmp) / Math.PI;
                    QAngle angle = new QAngle((float)pitch, (float)yaw, 0);

                    Server.NextFrame(() => {
                        if (pitch < 15 && pitch > -15)
                        {
                            bot.PlayerPawn.Value.Teleport(null, angle, null);
                        }
                        bot.PlayerPawn.Value.EyeAngles.X = (float)pitch;
                        bot.PlayerPawn.Value.EyeAngles.Y = (float)yaw;
                    });
                }
            }
        }
    }

}
