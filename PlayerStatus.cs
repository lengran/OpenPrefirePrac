using CounterStrikeSharp.API.Core;

namespace OpenPrefirePrac;

public class PlayerStatus
{
    public int practice_no;     // -1 if player is not practicing

    public int progress;

    public int healing_method;  // 0: No healing; 1: Init hp 500 with no healing; 2: +25hp for each kill; 3: +100hp for each kill; 4: +500hp for each kill

    public List<CCSPlayerController> bots;

    public Dictionary<string, int> localized_practice_names;

    public Dictionary<string, int> localized_difficulty_names;

    public int training_mode;   // 0: Random mode, 70% targets; 1: Full mode, all targets.

    public Dictionary<string, int> localized_training_mode_names;
    
    public List<int> enabled_targets;
    
    public List<int> beams;

    public PlayerStatus()
    {
        practice_no = -1;
        
        progress = 0;
        healing_method = 3;
        bots = new List<int>();
        training_mode = 0;
        enabled_targets = new List<int>();
        beams = new List<int>();

        // Do not populate these now so as to support changing languages dynamically(TODO)
        localized_practice_names = new Dictionary<string, int>();
        localized_difficulty_names = new Dictionary<string, int>();
        localized_training_mode_names = new Dictionary<string, int>();
    }
}