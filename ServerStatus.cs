namespace OpenPrefirePrac;

public class ServerStatus
{
    public readonly Dictionary<string, bool> BoolConvars = new();

    public readonly Dictionary<string, int> IntConvars = new();

    public readonly Dictionary<string, float> FloatConvars = new();

    public readonly Dictionary<string, string> StringConvars = new();

    public bool WarmupStatus = false;
}