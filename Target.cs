using System.Runtime.CompilerServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using System.Collections.Generic;
using System.Numerics;
using CounterStrikeSharp.API.Modules.Utils;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace OpenPrefirePrac;
public class TargetBot
{
    public Vector position;
    public QAngle rotation;
    public bool is_crouching;

    public TargetBot(float p_x, float p_y, float p_z, float a_x, float a_y, float a_z, bool crouching)
    {
        position = new Vector(p_x, p_y, p_z);
        rotation = new QAngle(a_x, a_y, a_z);
        is_crouching = crouching;
    }
}

public class PrefirePractice
{
    public List<TargetBot> targets;
    public TargetBot player;
    public string practice_name;
    public List<string> incompatible_practices;
    public int num_bots;
    public float spawn_ratio;

    public List<Vector> guiding_line;

    public PrefirePractice(string map, string practice)
    {
        targets = new List<TargetBot>();
        incompatible_practices = new List<string>();
        guiding_line = new List<CounterStrikeSharp.API.Modules.Utils.Vector>();

        // Construct a practice from the description file.
        practice_name = practice;
        string path = $"../../csgo/addons/counterstrikesharp/plugins/OpenPrefirePrac/maps/{map}/{practice}.txt";
        try
        {
            using (StreamReader sr = File.OpenText(path))
            {
                string s;
                string[] w;
                char[] delimiter_chars = { ' ', ',', '\t' };

                // The first line contains incompatible practices.
                s = sr.ReadLine();
                w = s.Split(delimiter_chars);
                for (int i = 0; i < w.Length; i++)
                    incompatible_practices.Add(w[i]);

                // The second line indicates how many bots are needed, and the spawn ratio.
                s = sr.ReadLine();
                w = s.Split(delimiter_chars);
                num_bots = Convert.ToInt32(w[0]);
                spawn_ratio = Convert.ToSingle(w[1]);

                // The third line contains player's position and rotation.
                s = sr.ReadLine();
                w = s.Split(delimiter_chars);
                player = new TargetBot(Convert.ToSingle(w[0]), Convert.ToSingle(w[1]), Convert.ToSingle(w[2]), Convert.ToSingle(w[3]), Convert.ToSingle(w[4]), Convert.ToSingle(w[5]), false);

                while ((s = sr.ReadLine()) != null)
                {
                    w = s.Split(delimiter_chars);
                    
                    // A line with 7 segments defines a target bot.
                    if (w.Length == 7)
                        targets.Add(new TargetBot(Convert.ToSingle(w[0]), Convert.ToSingle(w[1]), Convert.ToSingle(w[2]), Convert.ToSingle(w[3]), Convert.ToSingle(w[4]), Convert.ToSingle(w[5]), Convert.ToBoolean(w[6])));
                    
                    // A line with 3 real numbers defines a joint point of the guiding line.
                    if (w.Length == 3)
                        guiding_line.Add(new Vector(Convert.ToSingle(w[0]), Convert.ToSingle(w[1]), Convert.ToSingle(w[2])));
                }
            }
        }
        catch (System.Exception)
        {
            Console.WriteLine($"[OpenPrefirePrac] Reading practice file error: {path}");
        }
    }
}