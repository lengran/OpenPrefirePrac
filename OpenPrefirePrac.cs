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
using System.Text.Json;

namespace OpenPrefirePrac;

public class OpenPrefirePrac : BasePlugin
{
    public override string ModuleName => "Open Prefire Prac";
    public override string ModuleVersion => "0.1.27";
    public override string ModuleAuthor => "Lengran";
    public override string ModuleDescription => "A plugin for practicing prefire in CS2. https://github.com/lengran/OpenPrefirePrac";

    private readonly Dictionary<CCSPlayerController, PlayerStatus> _playerStatuses = new();
    
    private readonly Dictionary<CCSPlayerController, CCSPlayerController> _ownerOfBots = new();       // Map: bots -> owners.
    
    private readonly Dictionary<string, int> _practiceNameToId = new();
    
    private readonly Dictionary<int, bool> _practiceEnabled = new();
    
    private string _mapName = "";
    
    private int _playerCount;
    
    private readonly List<PrefirePractice> _practices = new();
    
    private readonly List<string> _availableMaps = new();

    private readonly ServerStatus _serverStatus = new();

    private CCSGameRules ?_serverGameRules;
    
    private Translator ?_translator;

    private readonly Dictionary<CCSPlayerController, int> _botRequests = new();         // make this thread-safe if necessary

    private DefaultConfig ?_defaultPlayerSettings;
    
    public override void Load(bool hotReload)
    {
        _playerCount = 0;

        _translator = new Translator(Localizer, ModuleDirectory, CultureInfo.CurrentCulture.Name);
        
	    Console.WriteLine("[OpenPrefirePrac] Registering listeners.");
        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServerHandler);
        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);

        LoadDefaultSettings();

        if (hotReload)
        {
            // Clear status registers
            _ownerOfBots.Clear();
            _practiceNameToId.Clear();
            _practiceEnabled.Clear();
            _practices.Clear();
            _availableMaps.Clear();
            _mapName = "";
            _playerCount = 0;
            _playerStatuses.Clear();
            
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
                if (!tempPlayer.IsValid || tempPlayer.IsBot || tempPlayer.IsHLTV)
                {
                    continue;
                }

                OnClientPutInServerHandler(tempPlayer.Slot);    
            }
        }
    }

    // TODO: Figure out if we can use the GameEventHandler attribute here instead
    // [GameEventHandler]
    public void OnClientPutInServerHandler(int slot)
    {
        var player = new CCSPlayerController(NativeAPI.GetEntityFromIndex(slot + 1));

        if (!player.IsValid || player.IsHLTV)
        {
            return;
        }

        if (player.IsBot)
        {
            // For bots: If someone is practicing and it's an unmanaged bot, add or kick the bot
            if (_playerCount > 0 && !_ownerOfBots.ContainsKey(player))
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
                    _playerStatuses[tmpPlayerNumBots.Key].Bots.Add(player);
                    _ownerOfBots.Add(player, tmpPlayerNumBots.Key);
                    Console.WriteLine($"[OpenPrefirePrac] Bot {player.PlayerName}, slot: {player.Slot} has been spawned.");
                }
                else
                {
                    // Already have enough bots, kick this bot.
                    Server.ExecuteCommand($"bot_kick {player.PlayerName}");
                    Console.WriteLine($"[OpenPrefirePrac] Exec command: bot_kick {player.PlayerName}");
                }
            }
        }
        else
        {
            // For players:
            _playerStatuses.Add(player, new PlayerStatus(_defaultPlayerSettings!));

            // Record player language
            _translator!.RecordPlayerCulture(player);
        }
    }

    [GameEventHandler]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (!_playerStatuses.ContainsKey(player))
            return HookResult.Continue;

        if (_playerStatuses[player].PracticeIndex != -1)
            ExitPrefireMode(player);

        // Release resources(practices, targets, bots...)
        _playerStatuses.Remove(player);

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
        
        if (!playerOrBot.IsValid || playerOrBot.IsHLTV)
        {
            return HookResult.Continue;
        }
        
        if (playerOrBot.IsBot)
        {
            
            if (_ownerOfBots.ContainsKey(playerOrBot))
            {
                // For managed bots
                var owner = _ownerOfBots[playerOrBot];
                var targetNo = _playerStatuses[owner].Progress;
                var practiceIndex = _playerStatuses[owner].PracticeIndex;

                if (targetNo < _playerStatuses[owner].EnabledTargets.Count)
                {
                    // If there are more targets to place, move bot to next place
                    _playerStatuses[owner].Progress++;

                    MovePlayer(playerOrBot,
                        _practices[practiceIndex].Targets[_playerStatuses[owner].EnabledTargets[targetNo]]
                            .IsCrouching,
                        _practices[practiceIndex].Targets[_playerStatuses[owner].EnabledTargets[targetNo]].Position,
                        _practices[practiceIndex].Targets[_playerStatuses[owner].EnabledTargets[targetNo]]
                            .Rotation);
                    
                    Server.NextFrame(() => FreezeBot(playerOrBot));
                }
                else
                {
                    // This code block is to patch the issue of extra bots.
                    // Explain:
                    //     Bot B is died while Bot A is still spawning, so progress 
                    //     is not updated in time. This could cause Bot B not being
                    //     kicked. So kick them here.
                    _ownerOfBots.Remove(playerOrBot);
                    _playerStatuses[owner].Bots.Remove(playerOrBot);
                    Server.ExecuteCommand($"bot_kick {playerOrBot.PlayerName}");

                    if (_playerStatuses[owner].Bots.Count == 0)
                    {
                        // Practice finished.
                        owner.PrintToChat(
                            $" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator!.Translate(owner, "practice.finish")}");
                        ExitPrefireMode(owner);
                    }
                }
            }
            // else
            // {
            //     // For unmanaged bots, kick them.
            //     Console.WriteLine($"[OpenPrefirePrac] Find an unmanaged bot ({playerOrBot.PlayerName}) spawning, kick it.");
            //     Server.ExecuteCommand($"bot_kick {playerOrBot.PlayerName}");
            // }
        }
        else
        {
            // For players: Set them up if they are practicing.
            if (!_playerStatuses.ContainsKey(playerOrBot))
                return HookResult.Continue;

            if (_playerStatuses[playerOrBot].PracticeIndex < 0)
                return HookResult.Continue;

            SetupPrefireMode(playerOrBot);
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var playerOrBot = @event.Userid;
        
        if (!playerOrBot.IsValid || playerOrBot.IsHLTV)
        {
            return HookResult.Continue;
        }
        
        if (playerOrBot.IsBot) 
        {
            if (_ownerOfBots.ContainsKey(playerOrBot))
            {
                // For managed bots
                var owner = _ownerOfBots[playerOrBot];
                var targetNo = _playerStatuses[owner].Progress;
                var practiceIndex = _playerStatuses[owner].PracticeIndex;

                if (targetNo >= _practices[practiceIndex].NumBots)         // Bots will be killed after their first time getting spawned, so as to move them to target spots.
                {
                    // Award the player.
                    if (owner.PawnIsAlive && owner.Pawn.Value != null)
                    {
                        owner.GiveNamedItem("item_assaultsuit");
                        RefillAmmo(owner);

                        if (_playerStatuses[owner].HealingMethod > 1)
                        {
                            var currentHp = owner.Pawn.Value.Health;
                            switch (_playerStatuses[owner].HealingMethod)
                            {
                                case 2:
                                    currentHp = currentHp + 25;
                                    break;
                                case 4:
                                    currentHp = currentHp + 500;
                                    break;
                                default:
                                    currentHp = currentHp + 100;
                                    break;
                            }
                            SetPlayerHealth(owner, currentHp);
                        }
                    }

                    // Print progress
                    owner.PrintToCenter(_translator!.Translate(owner, "practice.progress", _playerStatuses[owner].EnabledTargets.Count, _playerStatuses[owner].EnabledTargets.Count - targetNo + _playerStatuses[owner].Bots.Count - 1));
                }

                // Kick unnecessary bots
                if (targetNo >= _playerStatuses[owner].EnabledTargets.Count)
                {
                    _ownerOfBots.Remove(playerOrBot);
                    _playerStatuses[owner].Bots.Remove(playerOrBot);
                    Server.ExecuteCommand($"bot_kick {playerOrBot.PlayerName}");

                    if (_playerStatuses[owner].Bots.Count == 0)
                    {
                        // Practice finished.
                        owner.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator!.Translate(owner, "practice.finish")}");
                        ExitPrefireMode(owner);
                    }
                }
                else
                {
                    // Fast respawn
                    AddTimer(0.2f, () => {
                        if (playerOrBot.IsValid && !playerOrBot.PawnIsAlive)
                        {
                            playerOrBot.Respawn();
                        }
                    });
                }
            }
            // else
            // {
            //     // For unmanaged bots, kick them.
            //     Console.WriteLine($"[OpenPrefirePrac] Find an unmanaged bot ({playerOrBot.PlayerName}) dying, kick it.");
            //     Server.ExecuteCommand($"bot_kick {playerOrBot.PlayerName}");
            // }
        }
        else
        {
            // For players: If some bots have already been kicked, add them back.
            if (!_playerStatuses.ContainsKey(playerOrBot))
                return HookResult.Continue;
            
            var practiceIndex = _playerStatuses[playerOrBot].PracticeIndex;
            var numBots = _playerStatuses[playerOrBot].Bots.Count;
            
            if (practiceIndex > -1 && numBots < _practices[practiceIndex].NumBots)
            {
                _playerStatuses[playerOrBot].Progress = 0;
                AddBot(playerOrBot, _practices[practiceIndex].NumBots - numBots);
            }
        }
        
        return HookResult.Continue;
    }

    [ConsoleCommand("css_prefire", "Print available prefire routes and receive user's choice")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnPrefireCommand(CCSPlayerController player, CommandInfo commandInfo)
    {       
        var mainMenu = new ChatMenu(_translator!.Translate(player, "mainmenu.title"));

        mainMenu.AddMenuOption(_translator.Translate(player, "mainmenu.practice"), OpenPracticeMenu);
        mainMenu.AddMenuOption(_translator.Translate(player, "mainmenu.map"), OpenMapMenu);
        var currentDifficulty = _translator.Translate(player, $"difficulty.{_playerStatuses[player].HealingMethod}");
        mainMenu.AddMenuOption(_translator.Translate(player, "mainmenu.difficulty", currentDifficulty), OpenDifficultyMenu);
        var currentTrainingMode = _translator.Translate(player, $"modemenu.{_playerStatuses[player].TrainingMode}");
        mainMenu.AddMenuOption(_translator.Translate(player, "mainmenu.mode", currentTrainingMode), OpenModeMenu);
        mainMenu.AddMenuOption("Language preference", OpenLanguageMenu);
        mainMenu.AddMenuOption(_translator.Translate(player, "mainmenu.exit"), ForceExitPrefireMode);
        
        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, mainMenu);
        player.PrintToChat("===========================================");
    }

    public void OnRouteSelect(CCSPlayerController player, ChatMenuOption option)
    {
        if (_playerCount == 0)
        {
            SaveConvars();
            SetupConvars();
        }

        int practiceNo = _playerStatuses[player].LocalizedPracticeNames[option.Text];
        var previousPracticeNo = _playerStatuses[player].PracticeIndex;

        // Check if selected practice route is compatible with other on-playing routes.
        if (previousPracticeNo != practiceNo && !_practiceEnabled[practiceNo])
        {
            player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator!.Translate(player, "practice.incompatible")}");
            return;
        }

        
        if (previousPracticeNo != practiceNo)
        {
            // Update practice status
            if (previousPracticeNo > -1)
            {
                // Enable disabled practice routes
                for (var i = 0; i < _practices[previousPracticeNo].IncompatiblePractices.Count; i++)
                {
                    if (_practiceNameToId.ContainsKey(_practices[previousPracticeNo].IncompatiblePractices[i]))
                    {
                        var disabledPracticeNo = _practiceNameToId[_practices[previousPracticeNo].IncompatiblePractices[i]];
                        _practiceEnabled[disabledPracticeNo] = true;
                    }
                }
                _practiceEnabled[previousPracticeNo] = true;

                RemoveBots(player);
                DeleteGuidingLine(player);
            }
            else
            {
                _playerCount++;
            }

            _playerStatuses[player].PracticeIndex = practiceNo;

            // Disable incompatible practices.
            for (var i = 0; i < _practices[practiceNo].IncompatiblePractices.Count; i++)
            {
                if (_practiceNameToId.ContainsKey(_practices[practiceNo].IncompatiblePractices[i]))
                {
                    var disabledPracticeNo = _practiceNameToId[_practices[practiceNo].IncompatiblePractices[i]];
                    _practiceEnabled[disabledPracticeNo] = false;
                }
            }
            _practiceEnabled[practiceNo] = false;

            // Setup practice
            AddBot(player, _practices[practiceNo].NumBots);
            // DrawGuidingLine(player);
        }
        else
        {
            // If some bots have already been kicked, add them back.
            var numRemainingBots = _playerStatuses[player].Bots.Count;
            
            if (numRemainingBots < _practices[practiceNo].NumBots)
            {
                _playerStatuses[player].Progress = 0;
                AddBot(player, _practices[practiceNo].NumBots - numRemainingBots);
            }
        }
        

        // Practice begin
        SetupPrefireMode(player);
        var localizedPracticeName = _translator!.Translate(player, "map." + _mapName + "." + _practices[practiceNo].PracticeName);
        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator.Translate(player, "practice.choose", localizedPracticeName)}");
        player.PrintToCenter(_translator.Translate(player, "practice.begin"));
    }

    public void ForceExitPrefireMode(CCSPlayerController player, ChatMenuOption option)
    {
        ExitPrefireMode(player);
        
        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator!.Translate(player, "practice.exit")}");
    }

    public void OpenMapMenu(CCSPlayerController player, ChatMenuOption option)
    {
        var mapMenu = new ChatMenu(_translator!.Translate(player, "mapmenu.title"));
        foreach (var map in _availableMaps)
        {
            mapMenu.AddMenuOption(map, ChangeMap);
        }

        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, mapMenu);
        player.PrintToChat("===========================================");
    }

    public void ChangeMap(CCSPlayerController player, ChatMenuOption option)
    {
        // Only allow change map when nobody is practicing.
        if (_playerCount == 0)
        {
            Server.ExecuteCommand($"changelevel {option.Text}");
        }
        else
        {
            player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator!.Translate(player, "mapmenu.busy")}");
        }
    }

    public void OpenPracticeMenu(CCSPlayerController player, ChatMenuOption option)
    {
        // Dynamically draw menu
        var practiceMenu = new ChatMenu(_translator!.Translate(player, "practicemenu.title"));
        _playerStatuses[player].LocalizedPracticeNames.Clear();

        for (var i = 0; i < _practices.Count; i++)
        {
            if (_practiceEnabled[i])
            {
                var tmpLocalizedPracticeName = _translator.Translate(player, $"map.{_mapName}.{_practices[i].PracticeName}");
                _playerStatuses[player].LocalizedPracticeNames.Add(tmpLocalizedPracticeName, i);
                practiceMenu.AddMenuOption(tmpLocalizedPracticeName, OnRouteSelect); // practice name here is split by space instead of underline. TODO: Use localized text.
            }
        }
        int practiceNo = _playerStatuses[player].PracticeIndex;
        if (practiceNo > -1)
        {
            var tmpLocalizedPracticeName = _translator.Translate(player, $"map.{_mapName}.{_practices[practiceNo].PracticeName}");
            _playerStatuses[player].LocalizedPracticeNames.Add(tmpLocalizedPracticeName, practiceNo);
            practiceMenu.AddMenuOption(tmpLocalizedPracticeName, OnRouteSelect);
        }


        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, practiceMenu);
        player.PrintToChat("===========================================");
    }

    public void OpenDifficultyMenu(CCSPlayerController player, ChatMenuOption option)
    {
        // Dynamically draw menu
        var difficultyMenu = new ChatMenu(_translator!.Translate(player, "difficulty.title"));
        _playerStatuses[player].LocalizedDifficultyNames.Clear();

        for (var i = 0; i < 5; i++)
        {
            var tmpLocalizedDifficultyName = _translator.Translate(player, $"difficulty.{i}");
            _playerStatuses[player].LocalizedDifficultyNames.Add(tmpLocalizedDifficultyName, i);
            difficultyMenu.AddMenuOption(tmpLocalizedDifficultyName, OnDifficultyChosen); // practice name here is split by space instead of underline. TODO: Use localized text.
        }

        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, difficultyMenu);
        player.PrintToChat("===========================================");
    }

    public void OnDifficultyChosen(CCSPlayerController player, ChatMenuOption option)
    {
        var difficultyNo = _playerStatuses[player].LocalizedDifficultyNames[option.Text];
        _playerStatuses[player].HealingMethod = difficultyNo;
        var currentDifficulty = _translator!.Translate(player, $"difficulty.{difficultyNo}");
        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator.Translate(player, "difficulty.set", currentDifficulty)}");
    }

    public void OpenModeMenu(CCSPlayerController player, ChatMenuOption option)
    {
        var trainingModeMenu = new ChatMenu(_translator!.Translate(player, "modemenu.title"));
        _playerStatuses[player].LocalizedTrainingModeNames.Clear();

        for (var i = 0; i < 2; i++)
        {
            var tmpLocalizedTrainingModeName = _translator.Translate(player, $"modemenu.{i}");
            _playerStatuses[player].LocalizedTrainingModeNames.Add(tmpLocalizedTrainingModeName, i);
            trainingModeMenu.AddMenuOption(tmpLocalizedTrainingModeName, OnModeChosen);
        }

        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, trainingModeMenu);
        player.PrintToChat("===========================================");
    }

    public void OnModeChosen(CCSPlayerController player, ChatMenuOption option)
    {
        var trainingModeNo = _playerStatuses[player].LocalizedTrainingModeNames[option.Text];
        _playerStatuses[player].TrainingMode = trainingModeNo;
        var currentTrainingMode = _translator!.Translate(player, $"modemenu.{trainingModeNo}");
        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator!.Translate(player, "modemenu.set", currentTrainingMode)}");
    }

    public void OpenLanguageMenu(CCSPlayerController player, ChatMenuOption option)
    {
        // No need for localization here.
        var languageMenu = new ChatMenu("Change language settings");

        languageMenu.AddMenuOption("English", OnLanguageChosen);
        languageMenu.AddMenuOption("Português", OnLanguageChosen);
        languageMenu.AddMenuOption("中文", OnLanguageChosen);

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
    }

    private void LoadPractice()
    {
        Console.WriteLine($"[OpenPrefirePrac] Loading practices for map {_mapName}.");
        var practiceFiles = new List<string>(Directory.EnumerateFiles($"{ModuleDirectory}/maps/{_mapName}"));
        _practices.Clear();
        _practiceNameToId.Clear();
        _practiceEnabled.Clear();
        for (var i = 0; i < practiceFiles.Count; i++)
        {
            var practiceName = practiceFiles[i].Substring(practiceFiles[i].LastIndexOf(Path.DirectorySeparatorChar) + 1).Split(".")[0];
            _practices.Add(new PrefirePractice(ModuleDirectory, _mapName, practiceName));
            _practiceNameToId.Add(practiceName, i);
            _practiceEnabled.Add(i, true);
            Console.WriteLine($"[OpenPrefirePrac] {_mapName} {practiceName} Loaded.");
        }
    }
    
    private void ExitPrefireMode(CCSPlayerController player)
    {
        var previousPracticeNo = _playerStatuses[player].PracticeIndex;
        if (previousPracticeNo > -1)
        {
            RemoveBots(player);
            DeleteGuidingLine(player);

            // Enable disabled practice routes
            for (var i = 0; i < _practices[previousPracticeNo].IncompatiblePractices.Count; i++)
            {
                if (_practiceNameToId.TryGetValue(_practices[previousPracticeNo].IncompatiblePractices[i], out var value))
                {
                    _practiceEnabled[value] = true;
                }
            }
            _practiceEnabled[previousPracticeNo] = true;

            _playerStatuses[player].PracticeIndex = -1;
            _playerCount--;
        }
        
        if (_playerCount == 0)
        {
            // Server.ExecuteCommand("sv_cheats 0");
            // Server.ExecuteCommand("mp_warmup_pausetimer 0");
            // Server.ExecuteCommand("bot_quota_mode competitive");
            // Server.ExecuteCommand("tv_enable 1");
            // Server.ExecuteCommand("weapon_auto_cleanup_time 0");
            // Server.ExecuteCommand("mp_buytime 20");
            // Server.ExecuteCommand("mp_maxmoney 16000");
            // Server.ExecuteCommand("mp_startmoney 16000");
            // Server.ExecuteCommand("mp_buy_anywhere 0");
            // Server.ExecuteCommand("mp_free_armor 0");
            // // Server.ExecuteCommand("mp_roundtime 1.92");
            // // Server.ExecuteCommand("mp_roundtime_defuse 1.92");
            // // Server.ExecuteCommand("mp_team_intro_time 6.5");
            // // Server.ExecuteCommand("mp_freezetime 15");
            // // Server.ExecuteCommand("mp_ignore_round_win_conditions 0");
            // // Server.ExecuteCommand("mp_respawn_on_death_ct 0");
            // // Server.ExecuteCommand("mp_respawn_on_death_t 0");
            // Server.ExecuteCommand("sv_alltalk 1");
            // Server.ExecuteCommand("sv_full_alltalk 1");
            // Server.ExecuteCommand("mp_warmup_start");

            RestoreConvars();
        }
    }

    private void ResetBots(CCSPlayerController player)
    {
        _playerStatuses[player].Progress = 0;

        for (var i = 0; i < _playerStatuses[player].Bots.Count; i++)
        {
            var bot = _playerStatuses[player].Bots[i];
            if (bot.IsValid || bot.PawnIsAlive)
            {
                // Server.ExecuteCommand($"bot_kill {bot.PlayerName}");
                KillBot(bot);
            }
            else
            {
                Console.WriteLine($"[OpenPrefirePrac] Error: Player has an invalid bot.(slot: {i})");
            }
        }
    }

    private void SetupPrefireMode(CCSPlayerController player)
    {
        var practiceNo = _playerStatuses[player].PracticeIndex;
        
        GenerateRandomPractice(player);
        AddTimer(0.5f, () => ResetBots(player));

        DeleteGuidingLine(player);
        DrawGuidingLine(player);
        
        // Setup player's HP
        if (_playerStatuses[player].HealingMethod == 1 || _playerStatuses[player].HealingMethod == 4)
            AddTimer(0.5f, () => SetPlayerHealth(player, 500));
        AddTimer(1f, () => EquipPlayer(player));
        AddTimer(1.5f, () => MovePlayer(player, false, _practices[practiceNo].Player.Position, _practices[practiceNo].Player.Rotation));
    }

    private void RemoveBots(CCSPlayerController player)
    {
        foreach (var bot in _playerStatuses[player].Bots)
        {
            if (bot.IsValid)
            {
                Server.ExecuteCommand($"bot_kick {bot.PlayerName}");
            }
            else
            {
                Console.WriteLine($"[OpenPrefirePrac] Trying to kick an invalid bot.");
            }
            _ownerOfBots.Remove(bot);
        }
        _playerStatuses[player].Bots.Clear();
        _playerStatuses[player].Progress = 0;
    }

    private void AddBot(CCSPlayerController player, int numberOfBots)
    {
        Console.WriteLine($"[OpenPrefirePrac] Creating {numberOfBots} bots.");

        // Test a new method of adding bots
        _botRequests.Add(player, numberOfBots);

        for (var i = 0; i < numberOfBots; i++)
        {
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
        }

        // AddTimer(0.2f, () =>
        // {
        //     var numberBotToFind = numberOfBots;
        //     var playerEntities = Utilities.GetPlayers();

        //     foreach (var tempPlayer in playerEntities)
        //     {
        //         if (!tempPlayer.IsValid || !tempPlayer.IsBot || tempPlayer.IsHLTV) continue;
        //         if (!tempPlayer.UserId.HasValue) continue;
                
        //         // Check if it belongs to someone, if so, do nothing
        //         if (_ownerOfBots.ContainsKey(tempPlayer)) continue;

        //         // If it's a newly added bot
        //         if (numberBotToFind == 0)
        //         {
        //             // a redundent bot, kick it
        //             Server.ExecuteCommand($"bot_kick {tempPlayer.PlayerName}");
        //             Console.WriteLine($"[OpenPrefirePrac] Exec command: bot_kick {tempPlayer.PlayerName}");
        //             continue;
        //         }

        //         _playerStatuses[player].Bots.Add(tempPlayer);
        //         _ownerOfBots.Add(tempPlayer, player);

        //         numberBotToFind--;
                    
        //         Console.WriteLine($"[OpenPrefirePrac] Bot {tempPlayer.PlayerName}, slot: {tempPlayer.Slot} has been spawned.");
        //     }
        // });
    }

    private void MovePlayer(CCSPlayerController player, bool crouch, Vector pos, QAngle ang)
    {
        // Only bot can crouch
        if (crouch)
        {
            var movementService = new CCSPlayer_MovementServices(player.PlayerPawn.Value!.MovementServices!.Handle);
            AddTimer(0.1f, () => movementService.DuckAmount = 1);
            AddTimer(0.2f, () => player.PlayerPawn.Value.Bot!.IsCrouching = true);
        }
        
        player.PlayerPawn.Value!.Teleport(pos, ang, new Vector(0, 0, 0));
    }

    private void FreezeBot(CCSPlayerController? bot)
    {
        if (
            bot is { IsValid: true, IsBot: true, IsHLTV: false, PawnIsAlive: true } 
            && bot.Pawn.Value != null
        )
        {
            bot.Pawn.Value.MoveType = MoveType_t.MOVETYPE_OBSOLETE;
            Schema.SetSchemaValue(bot.Pawn.Value.Handle, "CBaseEntity", "m_nActualMoveType", 1);
            Utilities.SetStateChanged(bot.Pawn.Value, "CBaseEntity", "m_MoveType");
        }
    }

    private static void EquipPlayer(CCSPlayerController player)
    {
        if (!player.PawnIsAlive || player.Pawn.Value == null)
            return;
        
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

    private static void SetPlayerHealth(CCSPlayerController player, int hp)
    {
        if (!player.PawnIsAlive || player.Pawn.Value == null || hp < 0)
            return;
        
        if (hp > 100)
            player.Pawn.Value.MaxHealth = hp;
        player.Pawn.Value.Health = hp;
        Utilities.SetStateChanged(player.Pawn.Value, "CBaseEntity", "m_iHealth");
    }

    private void GenerateRandomPractice(CCSPlayerController player)
    {
        _playerStatuses[player].EnabledTargets.Clear();
        var practiceNo = _playerStatuses[player].PracticeIndex;
        
        for (var i = 0; i < _practices[practiceNo].Targets.Count; i++)
            _playerStatuses[player].EnabledTargets.Add(i);

        if (_playerStatuses[player].TrainingMode == 0)
        {
            // 0: Use part of the targets.
            var numTargets = (int)(_practices[practiceNo].SpawnRatio * _practices[practiceNo].Targets.Count);
            var rnd = new Random(DateTime.Now.Millisecond);

            var numToRemove = _practices[practiceNo].Targets.Count - numTargets;
            for (var i = 0; i < numToRemove; i++)
                _playerStatuses[player].EnabledTargets.RemoveAt(rnd.Next(_playerStatuses[player].EnabledTargets.Count));
        }
        // 1: Use all of the targets.
    }

    private void DrawGuidingLine(CCSPlayerController player)
    {
        var practiceNo = _playerStatuses[player].PracticeIndex;

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

            _playerStatuses[player].Beams.Add(beamIndex);
        }
    }

    private void DeleteGuidingLine(CCSPlayerController player)
    {
        for (var i = 0; i < _playerStatuses[player].Beams.Count; i++)
        {
            var beam = Utilities.GetEntityFromIndex<CBeam>(_playerStatuses[player].Beams[i]);

            if (beam == null || !beam.IsValid)
            {
                Console.WriteLine($"[OpenPrefirePrac] Error when deleting guiding line. Failed to get beam entity(index = {_playerStatuses[player].Beams[i]})");
                continue;
            }

            beam.Remove();
        }

        _playerStatuses[player].Beams.Clear();
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

        beam.Teleport(startPos, new QAngle(0, 0, 0), new Vector(0, 0, 0));
        beam.EndPos.Add(endPos);
        beam.DispatchSpawn();

        // Console.WriteLine($"[OpenPrefirePrac] Created a beam. Start position: {startPos}, end position: {endPos}, entity index: {beam.Index}");
        return (int)beam.Index;
    }

    // [ConsoleCommand("css_test", "For debug purpose.")]
    // [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    // public void OnTestCommand(CCSPlayerController player, CommandInfo commandInfo)
    // {
    //     LoadDefaultSetting();
    //     int defaultDifficulty = _defaultPlayerSettings!.Difficulty;
    //     int defaultTrainingMode = _defaultPlayerSettings!.TrainingMode;
    //     player.PrintToChat($"[DEBUG] DefaultDifficulty = {defaultDifficulty}, DefaultTrainingMode = {defaultTrainingMode}");
    // }

    private void SaveConvars()
    {
        string[] boolConvarNames = [
            "tv_enable",
            "bot_allow_grenades",
            "bot_allow_snipers",
            "bot_allow_shotguns",
            "mp_autoteambalance",
            "sv_alltalk",
            "sv_full_alltalk",
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
        ];

        string[] floatConvarNames = [
            "mp_respawn_immunitytime",
            "mp_buytime",
        ];

        string[] stringConvarNames = [
            "bot_quota_mode",
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
                    // Console.WriteLine($"[OpenPrefirePrac] {convarName}: {value}");
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
            Server.ExecuteCommand(convar.Key + " " + convar.Value.ToString());
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
        Server.ExecuteCommand("bot_allow_snipers 0");
        Server.ExecuteCommand("bot_allow_shotguns 0");
        Server.ExecuteCommand("mp_autoteambalance 0");
        Server.ExecuteCommand("sv_alltalk 1");
        Server.ExecuteCommand("sv_full_alltalk 1");

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

        Server.ExecuteCommand("mp_respawn_immunitytime -1");
        Server.ExecuteCommand("mp_buytime 9999");

        Server.ExecuteCommand("bot_quota_mode normal");
        
        Server.ExecuteCommand("mp_warmup_start");
        Server.ExecuteCommand("bot_kick all");

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
        if (!player.IsValid || !player.PawnIsAlive || player.Pawn == null || player.Pawn.Value == null || player.Pawn.Value.WeaponServices == null)
        {
            return;
        }

        var weapons = player.Pawn.Value.WeaponServices.MyWeapons;
        foreach (var weapon in weapons)
        {
            if (weapon.IsValid && weapon != null && weapon.Value != null && weapon.Value.DesignerName.Length != 0 && !weapon.Value.DesignerName.Contains("knife") && !weapon.Value.DesignerName.Contains("bayonet"))
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

    private void LoadDefaultSettings()
    {
        string path = $"{ModuleDirectory}/default_cfg.json";

        // Read default settings from PlayerStatus.cs
        PlayerStatus tmpStatus = new PlayerStatus();
        int tmpDifficulty = tmpStatus.HealingMethod;
        int tmpTrainingMode = tmpStatus.TrainingMode;

        if (!File.Exists(path))
        {
            // Use default settings
            Console.WriteLine("[OpenPrefirePrac] No default settings provided. Will use default settings.");
        }
        else
        {
            // Load settings from default_cfg.json
            var options = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };

            string jsonString = File.ReadAllText(path);
            
            try
            {
                DefaultConfig jsonConfig = JsonSerializer.Deserialize<DefaultConfig>(jsonString, options)!;

                tmpDifficulty = jsonConfig.Difficulty;
                tmpTrainingMode = jsonConfig.TrainingMode;

                Console.WriteLine($"[OpenPrefirePrac] Successfully load default settings. Difficulty = {tmpDifficulty}, TrainingMode = {tmpTrainingMode}");
            }
            catch (System.Exception)
            {
                Console.WriteLine("[OpenPrefirePrac] Failed to load default settings. Will use default settings.");
            }
        }

        _defaultPlayerSettings = new DefaultConfig(tmpDifficulty, tmpTrainingMode);
    }
}
