using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using Microsoft.Extensions.Localization;
using System.Globalization;
using MaxMind.GeoIP2;
using CounterStrikeSharp.API.Modules.Utils;

namespace OpenPrefirePrac;

public class Translator
{
    private IStringLocalizer _localizer;
    private string _pluginPath;
    private string _defaultCulture;

    private Dictionary<string, string> _countryToCultureMapper = new();

    // Replace Dictionary with PlayerLanguageManager if that is better.
    private readonly Dictionary<ulong, string> _languageManager = new();

    public Translator(IStringLocalizer localizer, string moduleDirectory, string defaultCulture)
    {
        _localizer = localizer;
        _pluginPath = moduleDirectory;
        _defaultCulture = defaultCulture;

        // Whenever add a translation profile, add a mapper.
        _countryToCultureMapper.Add("CN", "ZH");
        _countryToCultureMapper.Add("BR", "pt-BR");
    }

    public void RecordPlayerCulture(CCSPlayerController player)
    {
        // TODO: Find a way to make this compatible with the !lang command.
        // System.Globalization.CultureInfo language = player.GetLanguage();

        var steamId = player.SteamID;

        // If the player has already been registered, do nothing.
        if (_languageManager.ContainsKey(steamId))
            return;
        var playerIp = GetPlayerIp(player);
        if (playerIp == null)
        {
            _languageManager.Add(steamId, _defaultCulture);
            return;
        }

        var isoCode = GetPlayerIsoCode(playerIp);
        if (isoCode == null)
        {
            _languageManager.Add(steamId, _defaultCulture);
            return;
        }

        // Languages are mapped from country codes. So if there is no mapper for a player's country, use default language(English).
        if (_countryToCultureMapper.ContainsKey(isoCode))
            _languageManager.Add(steamId, _countryToCultureMapper[isoCode]);
        else
            _languageManager.Add(steamId, _defaultCulture);
    }

    public string Translate(CCSPlayerController player, string tokenToLocalize)
    {
        var steamId = player.SteamID;

        var playerCulture = _languageManager[steamId];

        using (new WithTemporaryCulture(CultureInfo.GetCultureInfo(playerCulture)))
        {
            return ParseMsg(_localizer[tokenToLocalize]);
        }
    }

    public string Translate(CCSPlayerController player, string tokenToLocalize, params object[] arguments)
    {
        var steamId = player.SteamID;

        var playerCulture = _languageManager[steamId];

        using (new WithTemporaryCulture(CultureInfo.GetCultureInfo(playerCulture)))
        {
            return ParseMsg(_localizer[tokenToLocalize, arguments]);
        }
    }

    public void UpdatePlayerCulture(ulong steamId, string cultureCode)
    {
        _languageManager[steamId] = cultureCode;
    }

    private string ParseMsg(string coloredMsg)
    {
        return coloredMsg
            .Replace("[GREEN]", " " + ChatColors.Green.ToString())
            .Replace("[RED]", " " + ChatColors.Red.ToString())
            .Replace("[YELLOW]", " " + ChatColors.Yellow.ToString())
            .Replace("[BLUE]", " " + ChatColors.Blue.ToString())
            .Replace("[PURPLE]", " " + ChatColors.Purple.ToString())
            .Replace("[ORANGE]", " " + ChatColors.Orange.ToString())
            .Replace("[WHITE]", " " + ChatColors.White.ToString())
            .Replace("[NORMAL]", " " + ChatColors.White.ToString())
            .Replace("[GREY]", " " + ChatColors.Grey.ToString())
            .Replace("[LIGHT_RED]", " " + ChatColors.LightRed.ToString())
            .Replace("[LIGHT_BLUE]", " " + ChatColors.LightBlue.ToString())
            .Replace("[LIGHT_PURPLE]", " " + ChatColors.LightPurple.ToString())
            .Replace("[LIGHT_YELLOW]", ChatColors.LightYellow.ToString())
            .Replace("[DARK_RED]", " " + ChatColors.DarkRed.ToString())
            .Replace("[DARK_BLUE]", " " + ChatColors.DarkBlue.ToString())
            .Replace("[BLUE_GREY]", " " + ChatColors.BlueGrey.ToString())
            .Replace("[OLIVE]", " " + ChatColors.Olive.ToString())
            .Replace("[LIME]", " " + ChatColors.Lime.ToString())
            .Replace("[GOLD]", " " + ChatColors.Gold.ToString())
            .Replace("[SILVER]", " " + ChatColors.Silver.ToString())
            .Replace("[MAGENTA]", " " + ChatColors.Magenta.ToString());
    }

    // These two functions are borrowed from https://github.com/aprox2/GeoLocationLanguageManagerPlugin/. Huge thanks!
    // Author: aprox2
    public static string? GetPlayerIp(CCSPlayerController player)
    {
        var playerIp = player.IpAddress;
        if (playerIp == null)
        {
            return null;
        }
        
        var parts = playerIp.Split(':');
        if (parts.Length == 2)
        {
            return parts[0];
        }
        else
        {
            return playerIp;
        }
    }

    public string? GetPlayerIsoCode(string ipAddress)
    {
        var geoDbPath = Path.Combine(_pluginPath, "GeoLite2-Country.mmdb");
        
        // check if the database file exists
        if (!File.Exists(geoDbPath))
        {
            Console.WriteLine("[OpenPrefirePrac] GeoLite2-Country.mmdb not found.");
            return null;
        }
        
        using var reader = new DatabaseReader(geoDbPath);
        try
        {
            var response = reader.Country(ipAddress);
            return response.Country.IsoCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[OpenPrefirePrac] Get Player's ISO code failed: " + ex.Message);
            return null;
        }
    }
}
