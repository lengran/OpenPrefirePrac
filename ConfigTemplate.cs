namespace OpenPrefirePrac;

public class DefaultConfig
{
    public int ?Difficulty { get; set; }

    public int ?TrainingMode { get; set; }

    public int ?BotWeapon {get; set; }

    public DefaultConfig(int difficulty, int trainingMode, int botWeapon)
    {
        Difficulty = difficulty;
        TrainingMode = trainingMode;
        BotWeapon = botWeapon;
    }
}