using System.Text.Json;

namespace OpenPrefirePrac;

public class DefaultConfig
{
    public int Difficulty { get; set; } = 3;

    public int TrainingMode { get; set; } = 0;

    public int BotWeapon {get; set; } = 0;

    /*
     * 0 = disabled.
     * 1 = css based aim lock.
     * 2 = behavior tree based aim lock (hard).
    */
    public int BotAimLock {get; set; } = 1;

    private string _moduleDirectory = "";

    public DefaultConfig()
    {
        // DeserializeConstructor
    }

    public DefaultConfig(string moduleDirectory)
    {
        _moduleDirectory = moduleDirectory;
    }
    
    public void LoadDefaultSettings()
    {
        string path = $"{_moduleDirectory}/default_cfg.json";

        if (!File.Exists(path))
        {
            // Use default settings
            Console.WriteLine("[OpenPrefirePrac] No custom settings provided. Will use default settings.");
        }
        else
        {
            // Load settings from default_cfg.json
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,

            };

            string jsonString = File.ReadAllText(path);
            
            try
            {
                DefaultConfig jsonConfig = JsonSerializer.Deserialize<DefaultConfig>(jsonString, options)!;

                if (jsonConfig.Difficulty > -1 && jsonConfig.Difficulty < 6)
                {
                    Difficulty = jsonConfig.Difficulty;
                }

                if (jsonConfig.TrainingMode > -1 && jsonConfig.TrainingMode < 2)
                {
                    TrainingMode = jsonConfig.TrainingMode;
                }
                
                if (jsonConfig.BotWeapon > -1 && jsonConfig.BotWeapon < 5)
                {
                    BotWeapon = jsonConfig.BotWeapon;
                }

                BotAimLock = jsonConfig.BotAimLock;

                Console.WriteLine($"[OpenPrefirePrac] Using default settings: Difficulty = {Difficulty}, TrainingMode = {TrainingMode}, BotWeapon = {BotWeapon}, BotAimLock = {BotAimLock}");
            }
            catch (System.Exception)
            {
                Console.WriteLine("[OpenPrefirePrac] Failed to load custom settings. Will use default settings.");
            }
        }
    }
}