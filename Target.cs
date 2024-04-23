using CounterStrikeSharp.API.Modules.Utils;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace OpenPrefirePrac;
public class TargetBot
{
    public readonly Vector Position;
    public readonly QAngle Rotation;
    public readonly bool IsCrouching;

    public TargetBot(float pX, float pY, float pZ, float aX, float aY, float aZ, bool isCrouching)
    {
        Position = new Vector(pX, pY, pZ);
        Rotation = new QAngle(aX, aY, aZ);
        IsCrouching = isCrouching;
    }
}

public class PrefirePractice
{
    public readonly List<TargetBot> Targets;
    public readonly TargetBot Player;
    public readonly string PracticeName;
    public readonly List<string> IncompatiblePractices;
    public readonly int NumBots;
    public readonly float SpawnRatio;
    public readonly List<Vector> GuidingLine;
    private static readonly char[] Delimiters = { ' ', ',', '\t' };

    public PrefirePractice(string moduleDirectory, string map, string practice)
    {
        Targets = new List<TargetBot>();
        IncompatiblePractices = new List<string>();
        GuidingLine = new List<Vector>();

        // Construct a practice from the description file.
        PracticeName = practice;
        var path = $"{moduleDirectory}/maps/{map}/{practice}.txt";
        // Console.WriteLine("[OpenPrefirePrac] Reading practice file: " + path);
        
        using var sr = File.OpenText(path);

        // The first line contains incompatible practices.
        var s = sr.ReadLine();
        if (s == null)
        {
            throw new Exception($"[OpenPrefirePrac] Reading practice file error (1st line): {path}");
        }
            
        var w = s.Split(Delimiters);
        for (var i = 0; i < w.Length; i++)
        {
            IncompatiblePractices.Add(w[i]);
        }

        // The second line indicates how many bots are needed, and the spawn ratio.
        s = sr.ReadLine();
        if (s == null)
        {
            throw new Exception($"[OpenPrefirePrac] Reading practice file error (2nd line): {path}");
        }
            
        w = s.Split(Delimiters);
        NumBots = Convert.ToInt32(w[0]);
        SpawnRatio = Convert.ToSingle(w[1]);

        // The third line contains player's position and rotation.
        s = sr.ReadLine();
        if (s == null)
        {
            throw new Exception($"[OpenPrefirePrac] Reading practice file error (3rd line): {path}");
        }
            
        w = s.Split(Delimiters);
        Player = new TargetBot(Convert.ToSingle(w[0]), Convert.ToSingle(w[1]), Convert.ToSingle(w[2]), Convert.ToSingle(w[3]), Convert.ToSingle(w[4]), Convert.ToSingle(w[5]), false);

        while ((s = sr.ReadLine()) != null)
        {
            w = s.Split(Delimiters);
            
            // A line with 7 segments defines a target bot. Comments will be ignored.
            if (w.Length >= 7)
                Targets.Add(new TargetBot(Convert.ToSingle(w[0]), Convert.ToSingle(w[1]), Convert.ToSingle(w[2]), Convert.ToSingle(w[3]), Convert.ToSingle(w[4]), Convert.ToSingle(w[5]), Convert.ToBoolean(w[6])));
            
            // A line with 3 real numbers defines a joint point of the guiding line.
            if (w.Length == 3)
                GuidingLine.Add(new Vector(Convert.ToSingle(w[0]), Convert.ToSingle(w[1]), Convert.ToSingle(w[2])));
        }
    }
}
