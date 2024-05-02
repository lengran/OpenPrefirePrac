using CounterStrikeSharp.API.Core;

namespace OpenPrefirePrac;

public class PlayerStatus
{
    /**
     * -1 if player is not practicing
     */
    public int PracticeIndex = -1;
    
    public int Progress = 0;
    
    /**
     * 0: No healing
     * 1: Init hp 500 with no healing
     * 2: +25hp for each kill
     * 3: +100hp for each kill (default)
     * 4: +500hp for each kill
     */
    public int HealingMethod = 3;
    
    public readonly List<CCSPlayerController> Bots = new();
    public readonly Dictionary<string, int> LocalizedPracticeNames = new();
    public readonly Dictionary<string, int> LocalizedDifficultyNames = new();
    
    /**
     * 0: Random mode, randomly spawn some targets(the spawn ratio is specified in the practice profile)
     * 1: Full mode, all targets
     */
    public int TrainingMode = 0;
    
    public readonly Dictionary<string, int> LocalizedTrainingModeNames = new();

    public readonly List<int> EnabledTargets = new();
    public readonly List<int> Beams = new();

    /**
     * 0: Bots buy weapons randomly.
     * 1: Bots use UMP45.
     * 2: Bots use AK47.
     * 3: Bots use Scout.
     * 4: Bots use AWP.
     */
    public int BotWeapon = 0;

    public PlayerStatus(DefaultConfig defaultConfig)
    {
        HealingMethod = defaultConfig.Difficulty;
        TrainingMode = defaultConfig.TrainingMode;
        BotWeapon = defaultConfig.BotWeapon;
    }

    public PlayerStatus()
    {
        // Default constructor
    }
}
