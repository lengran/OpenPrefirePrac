namespace OpenPrefirePrac;

public class DefaultConfig
{
    public int Difficulty { get; set; } = 3;

    public int TrainingMode { get; set; } = 0;

    public int BotWeapon {get; set; } = 0;

    public DefaultConfig(int difficulty, int trainingMode, int botWeapon)
    {
        Difficulty = difficulty;
        TrainingMode = trainingMode;
        BotWeapon = botWeapon;
    }
}