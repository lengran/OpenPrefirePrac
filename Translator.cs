using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using Microsoft.Extensions.Localization;
using System.Globalization;
using MaxMind.GeoIP2;
using CounterStrikeSharp.API.Modules.Entities;

namespace OpenPrefirePrac;

public class Translator
{
    private IStringLocalizer _localizer;
    private string _plugin_path;
    
    private string _default_culture;

    private Dictionary<string, string> country_to_culture_mapper = new Dictionary<string, string>();

    // Replace Dictionary with PlayerLanguageManager if that is better.
    private Dictionary<ulong, string> language_manager = new Dictionary<ulong, string>();

    public Translator(IStringLocalizer localizer, string module_directory, string default_culture)
    {
        _localizer = localizer;
        _plugin_path = module_directory;
        _default_culture = default_culture;

        // Whenever add a translation profile, add a mapper.
        country_to_culture_mapper.Add("CN", "ZH");
        country_to_culture_mapper.Add("BR", "pt-BR");
    }

    public void RecordPlayerCulture(CCSPlayerController player)
    {
        // TODO: Find a way to make this compatible with the !lang command.
        // System.Globalization.CultureInfo language = player.GetLanguage();

        ulong steam_id = player.SteamID;

        // If the player has already been registered, do nothing.
        if (language_manager.ContainsKey(steam_id))
            return;
        var player_ip = GetPlayerIp(player);
        if (player_ip == null)
        {
            language_manager.Add(steam_id, _default_culture);
            return;
        }

        var iso_code = GetPlayerISOCode(player_ip);
        if (iso_code == null)
        {
            language_manager.Add(steam_id, _default_culture);
            return;
        }

        // Languages are mapped from country codes. So if there is no mapper for a player's country, use default language(English).
        if (country_to_culture_mapper.ContainsKey(iso_code))
            language_manager.Add(steam_id, country_to_culture_mapper[iso_code]);
        else
            language_manager.Add(steam_id, _default_culture);
    }

    public string Translate(CCSPlayerController player, string token_to_localize)
    {
        ulong steam_id = player.SteamID;

        string player_culture = language_manager[steam_id];

        using (new WithTemporaryCulture(CultureInfo.GetCultureInfo(player_culture)))
        {
            return _localizer[token_to_localize];
        }
    }

    public string Translate(CCSPlayerController player, string token_to_localize, params object[] arguments)
    {
        ulong steam_id = player.SteamID;

        string player_culture = language_manager[steam_id];

        using (new WithTemporaryCulture(CultureInfo.GetCultureInfo(player_culture)))
        {
            return _localizer[token_to_localize, arguments];
        }
    }

    public void UpdatePlayerCulture(ulong steam_id, string culture_code)
    {
        language_manager[steam_id] = culture_code;
    }

    // These two functions are borrowed from https://github.com/aprox2/GeoLocationLanguageManagerPlugin/. Huge thanks!
    public static string? GetPlayerIp(CCSPlayerController player)
    {
        var playerIp = player.IpAddress;
        if (playerIp == null) { return null; }
        string[] parts = playerIp.Split(':');
        if (parts.Length == 2)
        {
            return parts[0];
        }
        else
        {
            return playerIp;
        }
    }

    public string? GetPlayerISOCode(string ipAddress)
    {
        using var reader = new DatabaseReader(Path.Combine(_plugin_path, "GeoLite2-Country.mmdb"));
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