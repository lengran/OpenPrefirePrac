using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;
using System.Globalization;

namespace OpenPrefirePrac;

public class OpenPrefirePrac : BasePlugin
{
    public override string ModuleName => "Open Prefire Prac";
    
    public override string ModuleVersion => "0.0.15";

    private readonly Dictionary<CCSPlayerController, PlayerStatus> _playerManager = new();
    
    private readonly Dictionary<CCSPlayerController, CCSPlayerController> _mastersOfBots = new();
    
    private readonly Dictionary<string, int> _practiceNameToId = new();
    
    private readonly Dictionary<int, bool> _practiceEnabled = new();
    
    private string _mapName = "";
    
    private int _playerCount;
    
    private readonly List<PrefirePractice> _practices = new();
    
    private readonly List<string> _availableMaps = new();
    
    private readonly Translator _translator;

    public OpenPrefirePrac()
    {
        _playerCount = 0;
        _translator = new Translator(Localizer, ModuleDirectory, CultureInfo.CurrentCulture.Name);
    }
    
    public override void Load(bool hotReload)
    {
        base.Load(hotReload);

	    Console.WriteLine("[OpenPrefirePrac] Registering listeners.");
        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServerHandler);
        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);
        // RegisterListener<Listeners.OnClientDisconnectPost>(OnClientDisconnectHandler);

        if (hotReload)
        {
            // Clear status registers
            _mastersOfBots.Clear();
            _practiceNameToId.Clear();
            _practiceEnabled.Clear();
            _practices.Clear();
            _availableMaps.Clear();
            _mapName = "";
            _playerCount = 0;
            _playerManager.Clear();

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

        if (!player.IsValid || player.IsBot || player.IsHLTV)
        {
            return;
        }

        _playerManager.Add(player, new PlayerStatus());

        // Record player language
        _translator.RecordPlayerCulture(player);
    }

    // Don't know if this works. Can't test it myself. Need two people.
    // public void OnClientDisconnectHandler(int slot)
    // {
    //     var player = new CCSPlayerController(NativeAPI.GetEntityFromIndex(slot + 1));

    //     if (!player_manager.ContainsKey(slot))
    //         return;

    //     if (player_manager[slot].practice_no != -1)
    //         ExitPrefireMode(slot);

    //     // Release resources(practices, targets, bots...)
    //     player_manager.Remove(slot);
    // }

    [GameEventHandler]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        // Console.WriteLine($"[OpenPrefirePrac] Player {@event.Userid.PlayerName} disconnected.");
        // Still don't know if this works. I can't test this myself. Need two people.
        var player = @event.Userid;

        if (!_playerManager.ContainsKey(player))
            return HookResult.Continue;

        if (_playerManager[player].practice_no != -1)
            ExitPrefireMode(player);

        // Release resources(practices, targets, bots...)
        _playerManager.Remove(player);

        return HookResult.Continue;
    }

    public void OnMapStartHandler(string map)
    {
        _mapName = map;

        // load practices available in current map, from corresponding map directory.
        _availableMaps.Clear();
        List<string> map_dirs = new List<string>(Directory.EnumerateDirectories(ModuleDirectory + "/maps"));
        bool found = false;
        for (int i = 0; i < map_dirs.Count; i++)
        {
            string map_path = map_dirs[i].Substring(map_dirs[i].LastIndexOf(Path.DirectorySeparatorChar) + 1);
            Console.WriteLine($"[OpenPrefirePrac] Map folder for map {map_path} founded.");
            _availableMaps.Add(map_path);

            if (map_path.Equals(_mapName))
            {
                found = true;
                Console.WriteLine("[OpenPrefirePrac] Map folder for current map founded.");
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
        
        // For bots, set them up.
        if (!playerOrBot.IsValid || playerOrBot.IsHLTV)
        {
            return HookResult.Continue;
        }
        
        if (playerOrBot.IsBot)
        {
            // if there are more targets to place, move bot to next place
            if (_mastersOfBots.ContainsKey(playerOrBot))
            {
                var master = _mastersOfBots[@event.Userid];
                var target_no = _playerManager[master].progress;
                var practice_no = _playerManager[master].practice_no;

                if (target_no < _playerManager[master].enabled_targets.Count)
                {
                    _playerManager[master].progress++;

                    MovePlayer(@event.Userid,
                        _practices[practice_no].targets[_playerManager[master].enabled_targets[target_no]]
                            .is_crouching,
                        _practices[practice_no].targets[_playerManager[master].enabled_targets[target_no]].position,
                        _practices[practice_no].targets[_playerManager[master].enabled_targets[target_no]]
                            .rotation);
                    Server.ExecuteCommand($"css_freeze_helper {@event.Userid.Slot}");
                }
                else
                {
                    // This code block is to patch the issue of extra bots.
                    // Explain:
                    //     Bot B is died while Bot A is still spawning, so progress 
                    //     is not updated in time. This could cause Bot B not being
                    //     kicked. So kick them here.
                    _mastersOfBots.Remove(@event.Userid);
                    _playerManager[master].bots.Remove(@event.Userid);
                    Server.ExecuteCommand($"bot_kick {@event.Userid.PlayerName}");

                    if (_playerManager[master].bots.Count == 0)
                    {
                        // Practice finished.
                        master.PrintToChat(
                            $" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator.Translate(master, "practice.finish")}");
                        ExitPrefireMode(master);
                    }
                }
            }
        }
        else
        {
            // Unmanaged player. This should not happen since hot_reload is now supported.
            if (!_playerManager.ContainsKey(playerOrBot))
                return HookResult.Continue;

            if (_playerManager[playerOrBot].practice_no < 0)
                return HookResult.Continue;

            SetupPrefireMode(@event.Userid);
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (@event.Userid.IsValid && @event.Userid.IsBot && !@event.Userid.IsHLTV) 
        {
            if (_mastersOfBots.ContainsKey(@event.Userid.Slot))
            {
                int master_slot = _mastersOfBots[@event.Userid.Slot];
                int target_no = _playerManager[master_slot].progress;
                int practice_no = _playerManager[master_slot].practice_no;

                if (target_no >= _practices[practice_no].num_bots)         // Bots will be killed after their first time getting spawned, so as to move them to target spots.
                {
                    CCSPlayerController master = new CCSPlayerController(NativeAPI.GetEntityFromIndex(master_slot + 1));
                    
                    // Award the player.
                    if (master.PawnIsAlive && master.Pawn.Value != null  && _playerManager[master_slot].healing_method > 1)
                    {
                        master.GiveNamedItem("item_assaultsuit");
                        
                        int current_hp = master.Pawn.Value.Health;
                        // if (healing_method_of_players[master_slot] == 2)
                        //     current_hp = current_hp + 25;
                        // else
                        //     current_hp = current_hp + 100;
                        switch (_playerManager[master_slot].healing_method)
                        {
                            case 2:
                                current_hp = current_hp + 25;
                                break;
                            case 4:
                                current_hp = current_hp + 500;
                                break;
                            default:
                                current_hp = current_hp + 100;
                                break;
                        }
                        SetPlayerHealth(master, current_hp);
                    }

                    // Print progress
                    master.PrintToCenter(_translator.Translate(master, "practice.progress", _playerManager[master.Slot].enabled_targets.Count, _playerManager[master.Slot].enabled_targets.Count - target_no + _playerManager[master_slot].bots.Count - 1));
                }

                // Kick unnecessary bots
                if (target_no >= _playerManager[master_slot].enabled_targets.Count)
                {
                    _mastersOfBots.Remove(@event.Userid.Slot);
                    _playerManager[master_slot].bots.Remove(@event.Userid.Slot);
                    Server.ExecuteCommand($"bot_kick {@event.Userid.PlayerName}");

                    if (_playerManager[master_slot].bots.Count == 0)
                    {
                        // Practice finished.
                        var player = new CCSPlayerController(NativeAPI.GetEntityFromIndex(master_slot + 1));
                        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator.Translate(player, "practice.finish")}");
                        ExitPrefireMode(player.Slot);
                    }
                }
            }
        }

        // Check if player has enough bots for selected practice
        if (@event.Userid.IsValid && !@event.Userid.IsBot && !@event.Userid.IsHLTV)
        {
            if (!_playerManager.ContainsKey(@event.Userid.Slot))
                return HookResult.Continue;
            
            int practice_no = _playerManager[@event.Userid.Slot].practice_no;
            int num_bots = _playerManager[@event.Userid.Slot].bots.Count;
            
            if (practice_no > -1 && num_bots < _practices[practice_no].num_bots)
            {
                _playerManager[@event.Userid.Slot].progress = 0;
                AddBot(@event.Userid, _practices[practice_no].num_bots - num_bots);
            }
        }
        
        return HookResult.Continue;
    }

    [ConsoleCommand("css_prefire", "Print available prefire routes and receive user's choice")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnPrefireCommand(CCSPlayerController player, CommandInfo commandInfo)
    {       
        ChatMenu main_menu = new ChatMenu(_translator.Translate(player, "mainmenu.title"));

        main_menu.AddMenuOption(_translator.Translate(player, "mainmenu.practice"), OpenPracticeMenu);
        main_menu.AddMenuOption(_translator.Translate(player, "mainmenu.map"), OpenMapMenu);
        string current_difficulty = _translator.Translate(player, $"difficulty.{_playerManager[player.Slot].healing_method}");
        main_menu.AddMenuOption(_translator.Translate(player, "mainmenu.difficulty", current_difficulty), OpenDifficultyMenu);
        string current_training_mode = _translator.Translate(player, $"modemenu.{_playerManager[player.Slot].training_mode}");
        main_menu.AddMenuOption(_translator.Translate(player, "mainmenu.mode", current_training_mode), OpenModeMenu);
        main_menu.AddMenuOption("Language preference", OpenLanguageMenu);
        main_menu.AddMenuOption(_translator.Translate(player, "mainmenu.exit"), ForceExitPrefireMode);
        
        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, main_menu);
        player.PrintToChat("===========================================");
    }

    public void OnRouteSelect(CCSPlayerController player, ChatMenuOption option)
    {
        if (_playerCount == 0)
        {
            Server.ExecuteCommand("tv_enable 0");
            Server.ExecuteCommand("sv_cheats 1");
            Server.ExecuteCommand("mp_maxmoney 60000");
            Server.ExecuteCommand("mp_startmoney 60000");
            Server.ExecuteCommand("mp_buytime 9999");
            Server.ExecuteCommand("mp_buy_anywhere 1");
            Server.ExecuteCommand("bot_allow_grenades 0");
            Server.ExecuteCommand("bot_allow_snipers 0");
            Server.ExecuteCommand("bot_allow_shotguns 0");
            Server.ExecuteCommand("bot_difficulty 5");
            Server.ExecuteCommand("custom_bot_difficulty 5");
            Server.ExecuteCommand("sv_infinite_ammo 1");
            Server.ExecuteCommand("mp_limitteams 0");
            Server.ExecuteCommand("mp_autoteambalance 0");
            Server.ExecuteCommand("mp_warmup_pausetimer 1");
            Server.ExecuteCommand("bot_quota_mode normal");
            Server.ExecuteCommand("weapon_auto_cleanup_time 1");
            Server.ExecuteCommand("mp_free_armor 2");
            Server.ExecuteCommand("mp_respawn_immunitytime -1");
            // Server.ExecuteCommand("mp_roundtime 60");
            // Server.ExecuteCommand("mp_roundtime_defuse 60");
            // Server.ExecuteCommand("mp_freezetime 0");
            // Server.ExecuteCommand("mp_team_intro_time 0");
            // Server.ExecuteCommand("mp_ignore_round_win_conditions 1");
            // Server.ExecuteCommand("mp_respawn_on_death_ct 1");
            // Server.ExecuteCommand("mp_respawn_on_death_t 1");
            Server.ExecuteCommand("sv_alltalk 1");
            Server.ExecuteCommand("sv_full_alltalk 1");
            Server.ExecuteCommand("mp_warmup_start");
        }

        int practice_no = _playerManager[player.Slot].localized_practice_names[option.Text];

        // Check if selected practice route is compatible with other on-playing routes.
        if (!_practiceEnabled[practice_no])
        {
            player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator.Translate(player, "practice.incompatible")}");
            return;
        }

        // Update practice status
        int previous_practice_no = _playerManager[player.Slot].practice_no;
        if (previous_practice_no > -1)
        {
            // Enable disabled practice routes
            for (int i = 0; i < _practices[previous_practice_no].incompatible_practices.Count; i++)
            {
                if (_practiceNameToId.ContainsKey(_practices[previous_practice_no].incompatible_practices[i]))
                {
                    int disabled_practice_no = _practiceNameToId[_practices[previous_practice_no].incompatible_practices[i]];
                    _practiceEnabled[disabled_practice_no] = true;
                }
            }
        
            RemoveBots(player.Slot);
            DeleteGuidingLine(player.Slot);
        }
        else
        {
            _playerCount++;
        }

        _playerManager[player.Slot].practice_no = practice_no;

        // Disable incompatible practices.
        for (int i = 0; i < _practices[practice_no].incompatible_practices.Count; i++)
        {
            if (_practiceNameToId.ContainsKey(_practices[practice_no].incompatible_practices[i]))
            {
                int disabled_practice_no = _practiceNameToId[_practices[practice_no].incompatible_practices[i]];
                _practiceEnabled[disabled_practice_no] = false;
            }
        }

        // Setup practice
        AddBot(player, _practices[practice_no].num_bots);

        // Practice begin
        SetupPrefireMode(player);
        string localized_practice_name = _translator.Translate(player, "map." + _mapName + "." + _practices[practice_no].practice_name);
        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator.Translate(player, "practice.choose", localized_practice_name)}");
        player.PrintToCenter(_translator.Translate(player, "practice.begin"));
    }

    public void ForceExitPrefireMode(CCSPlayerController player, ChatMenuOption option)
    {
        ExitPrefireMode(player.Slot);
        
        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator.Translate(player, "practice.exit")}");
    }

    public void OpenMapMenu(CCSPlayerController player, ChatMenuOption option)
    {
        ChatMenu map_menu = new ChatMenu(_translator.Translate(player, "mapmenu.title"));
        for (int i = 0; i < _availableMaps.Count; i++)
            map_menu.AddMenuOption(_availableMaps[i], ChangeMap);

        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, map_menu);
        player.PrintToChat("===========================================");
    }

    public void ChangeMap(CCSPlayerController player, ChatMenuOption option)
    {
        // Only allow change map when noone is practicing.
        if (_playerCount == 0)
        {
            Server.ExecuteCommand($"changelevel {option.Text}");
        }
        else
        {
            player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White}{_translator.Translate(player, "mapmenu.busy")}");
        }
    }

    public void OpenPracticeMenu(CCSPlayerController player, ChatMenuOption option)
    {
        // Dynamically draw menu
        ChatMenu practice_menu = new ChatMenu(_translator.Translate(player, "practicemenu.title"));
        _playerManager[player.Slot].localized_practice_names.Clear();

        for (int i = 0; i < _practices.Count; i++)
        {
            if (_practiceEnabled[i])
            {
                string tmp_localized_practice_name = _translator.Translate(player, "map." + _mapName + "." + _practices[i].practice_name);
                _playerManager[player.Slot].localized_practice_names.Add(tmp_localized_practice_name, i);
                practice_menu.AddMenuOption(tmp_localized_practice_name, OnRouteSelect);     // practice name here is splited by space instead of underline. TODO: Use localized text.
            }
        }

        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, practice_menu);
        player.PrintToChat("===========================================");
    }

    public void OpenDifficultyMenu(CCSPlayerController player, ChatMenuOption option)
    {
        // Dynamically draw menu
        ChatMenu difficulty_menu = new ChatMenu(_translator.Translate(player, "difficulty.title"));
        _playerManager[player.Slot].localized_difficulty_names.Clear();

        for (int i = 0; i < 5; i++)
        {
            string tmp_localized_difficulty_name = _translator.Translate(player, $"difficulty.{i}");
            _playerManager[player.Slot].localized_difficulty_names.Add(tmp_localized_difficulty_name, i);
            difficulty_menu.AddMenuOption(tmp_localized_difficulty_name, OnDifficultyChosen);     // practice name here is splited by space instead of underline. TODO: Use localized text.
        }

        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, difficulty_menu);
        player.PrintToChat("===========================================");
    }

    public void OnDifficultyChosen(CCSPlayerController player, ChatMenuOption option)
    {
        int difficulty_no = _playerManager[player.Slot].localized_difficulty_names[option.Text];
        _playerManager[player.Slot].healing_method = difficulty_no;
        string current_difficulty = _translator.Translate(player, $"difficulty.{difficulty_no}");
        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator.Translate(player, "difficulty.set", current_difficulty)}");
    }

    public void OpenModeMenu(CCSPlayerController player, ChatMenuOption option)
    {
        ChatMenu training_mode_menu = new ChatMenu(_translator.Translate(player, "modemenu.title"));
        _playerManager[player.Slot].localized_training_mode_names.Clear();

        for (int i = 0; i < 2; i++)
        {
            string tmp_localized_training_mode_name = _translator.Translate(player, $"modemenu.{i}");
            _playerManager[player.Slot].localized_training_mode_names.Add(tmp_localized_training_mode_name, i);
            training_mode_menu.AddMenuOption(tmp_localized_training_mode_name, OnModeChosen);
        }

        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, training_mode_menu);
        player.PrintToChat("===========================================");
    }

    public void OnModeChosen(CCSPlayerController player, ChatMenuOption option)
    {
        int training_mode_no = _playerManager[player.Slot].localized_training_mode_names[option.Text];
        _playerManager[player.Slot].training_mode = training_mode_no;
        string current_training_mode = _translator.Translate(player, $"modemenu.{training_mode_no}");
        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator.Translate(player, "modemenu.set", current_training_mode)}");
    }

    public void OpenLanguageMenu(CCSPlayerController player, ChatMenuOption option)
    {
        // No need for localization here.
        ChatMenu language_menu = new ChatMenu("Change language settings");

        language_menu.AddMenuOption("English", OnLanguageChosen);
        language_menu.AddMenuOption("Português", OnLanguageChosen);
        language_menu.AddMenuOption("中文", OnLanguageChosen);

        player.PrintToChat("============ [OpenPrefirePrac] ============");
        MenuManager.OpenChatMenu(player, language_menu);
        player.PrintToChat("===========================================");
    }

    public void OnLanguageChosen(CCSPlayerController player, ChatMenuOption option)
    {
        switch (option.Text)
        {
            case "English":
                _translator.UpdatePlayerCulture(player.SteamID, "EN");
                break;
            case "Português":
                _translator.UpdatePlayerCulture(player.SteamID, "pt-BR");
                break;
            case "中文":
                _translator.UpdatePlayerCulture(player.SteamID, "ZH");
                break;
            default:
                _translator.UpdatePlayerCulture(player.SteamID, "EN");
                break;
        }

        player.PrintToChat($" {ChatColors.Green}[OpenPrefirePrac] {ChatColors.White} {_translator.Translate(player, "languagemenu.set")}");
    }

    private void LoadPractice()
    {
        Console.WriteLine($"[OpenPrefirePrac] Loading practices for map {_mapName}.");
        List<string> practice_files = new List<string>(Directory.EnumerateFiles(ModuleDirectory + "/maps/" + _mapName));
        _practices.Clear();
        _practiceNameToId.Clear();
        _practiceEnabled.Clear();
        for (int i = 0; i < practice_files.Count; i++)
        {
            string practice_name = practice_files[i].Substring(practice_files[i].LastIndexOf(Path.DirectorySeparatorChar) + 1).Split(".")[0];
            _practices.Add(new PrefirePractice(_mapName, practice_name));
            _practiceNameToId.Add(practice_name, i);
            _practiceEnabled.Add(i, true);
            Console.WriteLine($"[OpenPrefirePrac] {_mapName} {practice_name} Loaded.");
        }
    }
    
    private void ExitPrefireMode(CCSPlayerController player)
    {
        int previous_practice_no = _playerManager[player].practice_no;
        if (previous_practice_no > -1)
        {
            RemoveBots(player);
            DeleteGuidingLine(player);

            // Enable disabled practice routes
            for (int i = 0; i < _practices[previous_practice_no].incompatible_practices.Count; i++)
            {
                if (_practiceNameToId.ContainsKey(_practices[previous_practice_no].incompatible_practices[i]))
                {
                    int disabled_practice_no = _practiceNameToId[_practices[previous_practice_no].incompatible_practices[i]];
                    _practiceEnabled[disabled_practice_no] = true;
                }
            }

            _playerManager[player].practice_no = -1;
            _playerCount--;
        }
        
        if (_playerCount == 0)
        {
            Server.ExecuteCommand("sv_cheats 0");
            Server.ExecuteCommand("mp_warmup_pausetimer 0");
            Server.ExecuteCommand("bot_quota_mode competitive");
            Server.ExecuteCommand("tv_enable 1");
            Server.ExecuteCommand("weapon_auto_cleanup_time 0");
            Server.ExecuteCommand("mp_buytime 20");
            Server.ExecuteCommand("mp_maxmoney 16000");
            Server.ExecuteCommand("mp_startmoney 16000");
            Server.ExecuteCommand("mp_buy_anywhere 0");
            Server.ExecuteCommand("mp_free_armor 0");
            // Server.ExecuteCommand("mp_roundtime 1.92");
            // Server.ExecuteCommand("mp_roundtime_defuse 1.92");
            // Server.ExecuteCommand("mp_team_intro_time 6.5");
            // Server.ExecuteCommand("mp_freezetime 15");
            // Server.ExecuteCommand("mp_ignore_round_win_conditions 0");
            // Server.ExecuteCommand("mp_respawn_on_death_ct 0");
            // Server.ExecuteCommand("mp_respawn_on_death_t 0");
            Server.ExecuteCommand("sv_alltalk 1");
            Server.ExecuteCommand("sv_full_alltalk 1");
            Server.ExecuteCommand("mp_warmup_start");
        }
    }

    private void ResetBots(CCSPlayerController player)
    {
        _playerManager[player].progress = 0;

        for (var i = 0; i < _playerManager[player].bots.Count; i++)
        {
            var bot = _playerManager[player].bots[i];
            if (bot.IsValid || bot.PawnIsAlive)
            {
                Server.ExecuteCommand($"bot_kill {bot.PlayerName}");
            }
            else
            {
                Console.WriteLine($"[OpenPrefirePrac] Error: Player has an invalid bot.(slot: {i})");
            }
        }
    }

    private void SetupPrefireMode(CCSPlayerController player)
    {
        int practice_no = _playerManager[player].practice_no;
        
        GenerateRandomPractice(player.Slot);
        AddTimer(0.5f, () => ResetBots(player));

        DeleteGuidingLine(player);
        DrawGuidingLine(player.Slot);
        
        // Setup player's HP
        if (_playerManager[player.Slot].healing_method == 1 || _playerManager[player.Slot].healing_method == 4)
            AddTimer(0.5f, () => SetPlayerHealth(player, 500));
        AddTimer(1f, () => EquipPlayer(player));
        AddTimer(1.5f, () => MovePlayer(player, false, _practices[practice_no].player.position, _practices[practice_no].player.rotation));
    }

    private void RemoveBots(CCSPlayerController player)
    {
        for (int i = 0; i < _playerManager[player].bots.Count; i++)
        {
            int bot_slot = _playerManager[player].bots[i];
            var bot = new CCSPlayerController(NativeAPI.GetEntityFromIndex(bot_slot + 1));
            if (bot.IsValid)
            {
                Server.ExecuteCommand($"bot_kick {bot.PlayerName}");
            }
            else
            {
                Console.WriteLine($"[OpenPrefirePrac] Trying to kick an invalid bot.");
            }
            _mastersOfBots.Remove(bot_slot);
        }
        _playerManager[player].bots.Clear();
        _playerManager[player].progress = 0;
    }

    private void AddBot(CCSPlayerController player, int number_of_bots)
    {
        Console.WriteLine($"[OpenPrefirePrac] Creating {number_of_bots} bots.");
        for (int i = 0; i < number_of_bots; i++)
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

        AddTimer(0.4f, () =>
        {
            int number_bot_to_find = number_of_bots;
            var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller");

            foreach (var tempPlayer in playerEntities)
            {
                if (!tempPlayer.IsValid || !tempPlayer.IsBot || tempPlayer.IsHLTV) continue;
                if (tempPlayer.UserId.HasValue)
                {
                    // Chech if it belongs to someone, if so, do nothing
                    if (_mastersOfBots.ContainsKey(tempPlayer.Slot))
                        continue;

                    // If it's a newly added bot
                    if (number_bot_to_find == 0)
                    {
                        // a redundent bot, kick it
                        Server.ExecuteCommand($"bot_kick {tempPlayer.PlayerName}");
                        Console.WriteLine($"[OpenPrefirePrac] Exec command: bot_kick {tempPlayer.PlayerName}");
                        continue;
                    }

                    _playerManager[player.Slot].bots.Add(tempPlayer.Slot);
                    _mastersOfBots.Add(tempPlayer.Slot, player.Slot);

                    number_bot_to_find--;
                    
                    Console.WriteLine($"[OpenPrefirePrac] Bot {tempPlayer.PlayerName}, slot: {tempPlayer.Slot} has been spawned.");
                }
            }
        });
    }

    private void MovePlayer(CCSPlayerController player, bool crouch, Vector pos, QAngle ang)
    {
        // Only bot can crouch
        if (crouch)
        {
            CCSPlayer_MovementServices movement_service = new CCSPlayer_MovementServices(player.PlayerPawn.Value!.MovementServices!.Handle);
            AddTimer(0.1f, () => movement_service.DuckAmount = 1);
            AddTimer(0.2f, () => player.PlayerPawn.Value.Bot!.IsCrouching = true);
        }
        
        player.PlayerPawn.Value!.Teleport(pos, ang, new Vector(0, 0, 0));
    }

    // FreezeBot doesn't work in Event environment, so make it a command.
    [ConsoleCommand("css_freeze_helper", "Freeze a player")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnFreezeHelperCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        int bot_slot = int.Parse(commandInfo.ArgString);
        CCSPlayerController bot = new CCSPlayerController(NativeAPI.GetEntityFromIndex(bot_slot + 1));

        if (bot != null && bot.IsValid && bot.IsBot && !bot.IsHLTV && bot.PawnIsAlive && bot.Pawn.Value != null) // && bot.Pawn.Value.LifeState == (byte)LifeState_t.LIFE_ALIVE)
        {
            bot.Pawn.Value.MoveType = MoveType_t.MOVETYPE_OBSOLETE;
            Schema.SetSchemaValue(bot.Pawn.Value.Handle, "CBaseEntity", "m_nActualMoveType", 1);
            Utilities.SetStateChanged(bot.Pawn.Value, "CBaseEntity", "m_MoveType");
        }
    }

    private void EquipPlayer(CCSPlayerController player)
    {
        if (!player.PawnIsAlive || player.Pawn.Value == null)
            return;
        
        player.RemoveWeapons();

        // Give weapons and items
        player.GiveNamedItem("weapon_ak47");
        player.GiveNamedItem("weapon_deagle");
        player.GiveNamedItem("weapon_knife");
        player.GiveNamedItem("weapon_flashbang");
        player.GiveNamedItem("weapon_smokegrenade");
        player.GiveNamedItem("item_assaultsuit");

        // Switch to main weapon
        player.ExecuteClientCommand("slot1");
    }

    private void SetPlayerHealth(CCSPlayerController player, int hp)
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
        _playerManager[player].enabled_targets.Clear();
        int practice_no = _playerManager[player].practice_no;
        
        for (int i = 0; i < _practices[practice_no].targets.Count; i++)
            _playerManager[player].enabled_targets.Add(i);

        if (_playerManager[player].training_mode == 0)
        {
            // 0: Use part of the targets.
            int num_targets = (int)(_practices[practice_no].spawn_ratio * _practices[practice_no].targets.Count);
            Random rnd = new Random(DateTime.Now.Millisecond);

            int num_to_remove = _practices[practice_no].targets.Count - num_targets;
            for (int i = 0; i < num_to_remove; i++)
                _playerManager[player].enabled_targets.RemoveAt(rnd.Next(_playerManager[player].enabled_targets.Count));
        }
        // 1: Use all of the targets.
    }

    private void DrawGuidingLine(CCSPlayerController player)
    {
        var practice_no = _playerManager[player].practice_no;

        if (practice_no < 0 || practice_no >= _practices.Count)
        {
            Console.WriteLine($"[OpenPrefirePrac] Error when creating guiding line. Current practice_no illegal. (practice_no = {practice_no})");
            return;
        }

        if (_practices[practice_no].guiding_line.Count < 2)
            return;

        // Draw beams
        for (int i = 0; i < _practices[practice_no].guiding_line.Count - 1; i++)
        {
            int beam_index = DrawBeam(_practices[practice_no].guiding_line[i], _practices[practice_no].guiding_line[i + 1]);
            
            if (beam_index == -1)
                return;

            _playerManager[player].beams.Add(beam_index);
        }
    }

    private void DeleteGuidingLine(CCSPlayerController player)
    {
        for (int i = 0; i < _playerManager[player].beams.Count; i++)
        {
            CBeam beam = Utilities.GetEntityFromIndex<CBeam>(_playerManager[player].beams[i]);

            if (beam == null || !beam.IsValid)
            {
                Console.WriteLine($"[OpenPrefirePrac] Error when deleting guiding line. Failed to get beam entity(index = {_playerManager[player].beams[i]})");
                continue;
            }

            beam.Remove();
        }

        _playerManager[player].beams.Clear();
    }

    private int DrawBeam(Vector startPos, Vector endPos)
    {
        CBeam beam = Utilities.CreateEntityByName<CBeam>("beam");
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
}
