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

    public PrefirePractice(string map, string practice)
    {
        targets = new List<TargetBot>();
        incompatible_practices = new List<string>();

        practice_name = practice;
        // TODO: Read positions from file
        string path = $"../../csgo/addons/counterstrikesharp/plugins/OpenPrefirePrac/maps/{map}/{practice}.txt";
        // Set player position and angles
        // player.SetBot(xyz,xyz,crouching)
        // for each line in file
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

                // The second line indicates how many bots are needed.
                s = sr.ReadLine();
                num_bots = Convert.ToInt32(s);

                // The third line contains player's position and rotation.
                s = sr.ReadLine();
                w = s.Split(delimiter_chars);
                player = new TargetBot(Convert.ToSingle(w[0]), Convert.ToSingle(w[1]), Convert.ToSingle(w[2]), Convert.ToSingle(w[3]), Convert.ToSingle(w[4]), Convert.ToSingle(w[5]), false);
                // Console.WriteLine("[OpenPrefirePrac] Player position loaded.");

                // The rest lines contain bots' info.
                while ((s = sr.ReadLine()) != null)
                {
                    w = s.Split(delimiter_chars);
                    // Console.WriteLine($"[OpenPrefirePrac] Bot position debug. words: {w}; sentence: {s}");
                    targets.Add(new TargetBot(Convert.ToSingle(w[0]), Convert.ToSingle(w[1]), Convert.ToSingle(w[2]), Convert.ToSingle(w[3]), Convert.ToSingle(w[4]), Convert.ToSingle(w[5]), Convert.ToBoolean(w[6])));
                }
            }
        }
        catch (System.Exception)
        {
            Console.WriteLine($"[OpenPrefirePrac] Reading practice file error: {path}");
        }
    }
}