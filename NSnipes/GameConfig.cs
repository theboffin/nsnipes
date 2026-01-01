using System.Text.Json;

namespace NSnipes;

public class GameConfig
{
    public string Initials { get; set; } = "AA";
    
    private static readonly string ConfigFilePath = "nsnipes.json";
    
    public static GameConfig Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                var config = JsonSerializer.Deserialize<GameConfig>(json);
                if (config != null && IsValidInitials(config.Initials))
                {
                    return config;
                }
            }
        }
        catch
        {
            // If loading fails, return default config
        }
        
        return new GameConfig();
    }
    
    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFilePath, json);
        }
        catch
        {
            // If saving fails, silently continue
        }
    }
    
    private static bool IsValidInitials(string initials)
    {
        if (string.IsNullOrEmpty(initials) || initials.Length != 2)
            return false;
            
        foreach (char c in initials)
        {
            if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                return false;
        }
        return true;
    }
}

