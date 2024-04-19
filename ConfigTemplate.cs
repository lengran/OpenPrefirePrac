namespace OpenPrefirePrac;

public class DefaultConfig
{
    public int Difficulty { get; set; }

    public int TrainingMode { get; set; }

    public DefaultConfig(int difficulty, int trainingMode)
    {
        Difficulty = difficulty;
        TrainingMode = trainingMode;
    }
}