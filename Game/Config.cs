using System.Reflection;
using System.Text;
using Foster.Framework;

namespace Game;

[AttributeUsage(AttributeTargets.Field)]
class ConfigValue : Attribute
{
    public object? RangeLow;
    public object? RangeHigh;

    public ConfigValue()
    {
        RangeLow = null;
        RangeHigh = null;
    }

    public ConfigValue(object rangeLow, object rangeHigh)
    {
        RangeLow = rangeLow;
        RangeHigh = rangeHigh;
    }
}

static class Config
{
    //
    // Values
    //

    [ConfigValue(250, int.MaxValue)]
    public static int WindowSizeX = 800;

    [ConfigValue(250, int.MaxValue)]
    public static int WindowSizeY = 600;

    [ConfigValue()]
    public static bool DoVerticalSync = true;

    [ConfigValue(15, 1000)]
    public static int MaxFrameRate = 240;

    [ConfigValue(15.0f, 120.0f)]
    public static float CameraFieldOfView = 75.0f;

    [ConfigValue(0.5f, 5.0f)]
    public static float MouseSensitivity = 4.0f;

    [ConfigValue(Level.MinimumViewDistance, 16)]
    public static int ViewDistance = 8;

    [ConfigValue(0.0f, 1.0f)]
    public static float VignetteStrength = 1;

    [ConfigValue()]
    public static bool DoTextureAA = true;

    [ConfigValue()]
    public static bool DoCloudShadows = true;

    [ConfigValue()]
    public static bool DoShadowMap = true;

    [ConfigValue(0, 64)]
    public static int TaskCountLimit = 10;

    [ConfigValue(0.0f, 2.0f)]
    public static float AudioMasterVolume = 1.0f;

    [ConfigValue(0.0f, 2.0f)]
    public static float AudioBlockVolume = 0.8f;

    [ConfigValue(0.0f, 2.0f)]
    public static float AudioStepVolume = 0.3f;

    //
    //
    //

    static bool UseDefault = false;

    static string GetConfigFilePath()
    {
        var exePath = Path.GetDirectoryName(AppContext.BaseDirectory) ?? "./";
        return Path.Join(exePath, "config.ini");
    }

    public static void ParseCommandLineArgs(string[] args)
    {
        if (args.Contains("-defaultconfig"))
        {
            UseDefault = true;
            Log.Warn("Using default config");
        }

        foreach (var arg in args)
        {
            if (!arg.Contains("-config:")) continue;
            var tokens = arg.TrimStart('-').Split(':');

            if (tokens.Length == 2 && tokens[0] == "config")
            {
                var ass = tokens[1].Split('=');
                if (ass.Length < 2) continue;
                Config.TrySetValue(ass[0], ass[1]);
            }
        }
    }

    public static void TrySetValue(string name, string value)
    {
        var field = typeof(Config).GetField(name);
        if (field == null) return;
        if (!field.HasAttr<ConfigValue>()) return;

        switch (field.GetValue(null))
        {
            case float:
                if (float.TryParse(value, out float parsedFloat))
                    field.SetValue(null, parsedFloat);
                else
                    Log.Error($"Failed to parse float value for field \"{field.Name}\"");
                break;
            case int:
                if (int.TryParse(value, out int parsedInt))
                    field.SetValue(null, parsedInt);
                else
                    Log.Error($"Failed to parse integer value for field \"{field.Name}\"");
                break;
            case bool:
                if (int.TryParse(value, out int parsedBool))
                    field.SetValue(null, parsedBool);
                else
                    Log.Error($"Failed to parse integer value for field \"{field.Name}\"");
                break;
            default:
                throw new Exception("Unhandled config value type");
        }
    }

    public static void ReadFromDisk()
    {
        if (UseDefault)
        {
            Log.Warn("Tried to read config from disk, but UseDefault is enabled");
            return;
        }

        string[] file;

        try
        {
            file = File.ReadAllLines(GetConfigFilePath());
        }
        catch (Exception e)
        {
            Log.Error($"Failed to read config file ({e.Message})");
            return;
        }

        if (file.Length == 0)
        {
            Log.Warn($"Read config file but found no entries");
            return;
        }

        Dictionary<string, string> entries = [];

        foreach (var line in file)
        {
            var tokens = line.Split('=');
            if (tokens.Length < 2) continue;
            entries[tokens[0]] = tokens[1];
        }

        var fields = typeof(Config).GetFields();

        foreach (var field in fields)
        {
            var gotValue = entries.TryGetValue(field.Name, out var source);
            if (!gotValue) continue;

            var attrib = (ConfigValue?)field.GetCustomAttribute(typeof(ConfigValue));
            if (attrib == null) continue;

            switch (field.GetValue(null))
            {
                case float:
                    if (float.TryParse(source, out float parsedFloat))
                        field.SetValue(null, Math.Clamp(parsedFloat, (float)attrib.RangeLow!, (float)attrib.RangeHigh!));
                    else
                        Log.Error($"Failed to parse float value for field \"{field.Name}\"");
                    break;
                case int:
                    if (Int32.TryParse(source, out int parsedInt))
                        field.SetValue(null, Math.Clamp(parsedInt, (int)attrib.RangeLow!, (int)attrib.RangeHigh!));
                    else
                        Log.Error($"Failed to parse integer value for field \"{field.Name}\"");
                    break;
                case bool:
                    if (bool.TryParse(source, out bool parsedBool))
                        field.SetValue(null, parsedBool);
                    else
                        Log.Error($"Failed to parse integer value for field \"{field.Name}\"");
                    break;
                default:
                    throw new Exception("Unhandled config value type");
            }
        }

        //
        Config.TaskCountLimit = Math.Min(Config.TaskCountLimit, Environment.ProcessorCount - 1);
    }

    public static void WriteToDisk()
    {
        if (UseDefault)
        {
            Log.Warn("Tried to write config to disk, but UseDefault is enabled");
            return;
        }
        
        StringBuilder output = new();

        var fields = typeof(Config).GetFields();

        foreach (var field in fields)
        {
            if (!field.HasAttr<ConfigValue>()) continue;

            switch (field.GetValue(null))
            {
                case float floatValue:
                    output.AppendLine($"{field.Name}={floatValue:0.000}");
                    break;
                case int intValue:
                    output.AppendLine($"{field.Name}={intValue}");
                    break;
                case bool boolValue:
                    output.AppendLine($"{field.Name}={boolValue}");
                    break;
                default:
                    throw new Exception("Unhandled config value type");
            }
        }

        File.WriteAllText(GetConfigFilePath(), output.ToString());
        Log.Info("Wrote config file to disk");
    }
}