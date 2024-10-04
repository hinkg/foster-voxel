using System.Numerics;
using Foster.Audio;
using Foster.Framework;

namespace Game;

class SoundData
{
    public List<Sound> Sounds;
    private int variationCounter;

    public SoundData()
    {
        Sounds = [];
        variationCounter = 0;
    }

    public bool Add(byte[] encodedData)
    {
        var format = AudioFormat.F32;
        var channels = 0;
        var sampleRate = 0;

        if (Sound.TryDecode(encodedData, ref format, ref channels, ref sampleRate, out var frameCount, out var decodedData))
        {
            var sound = new Sound(decodedData!, format, channels, sampleRate, frameCount);
            Sounds.Add(sound);
        }
        else
        {
            return false;
        }

        return true;
    }

    public void Play(float volume, AudioGroup group)
    {
        if (Sounds.Count == 0) return;

        var instance = Sounds[variationCounter].CreateInstance(Audio.GetSoundGroup(group));
        instance.Looping = false;
        instance.Volume = 0.05f * volume;
        instance.Play();

        variationCounter = (variationCounter + 1) % Sounds.Count;
    }

    public void Play(float volume, AudioGroup group, EntityVector position)
    {
        if (Sounds.Count == 0) return;

        var instance = Sounds[variationCounter].CreateInstance3d(Vector3.Zero, Audio.GetSoundGroup(group));
        instance.Looping = false;
        instance.Volume = 0.05f * volume;
        instance.Position = (position - Audio.ListenerPosition).ToVector3();
        instance.Play();

        variationCounter = (variationCounter + 1) % Sounds.Count;
    }
}

enum AudioGroup
{
    Master = 0,
    Block = 1,
    Step = 2,
}

static class Audio
{
    private static string AudioDirectory => Path.Join(Path.GetDirectoryName(AppContext.BaseDirectory) ?? ".\\", "Assets\\Audio");

    public static Dictionary<string, SoundData> SoundMap = [];

    public static EntityVector ListenerPosition = default;

    public static SoundGroup[] SoundGroups = [];

    public static SoundGroup? GetSoundGroup(AudioGroup group) => SoundGroups[(int)group];

    public static void Load()
    {
        Foster.Audio.Audio.Startup();
        Foster.Audio.Audio.Listener.WorldUp = Vector3.UnitZ;

        var audioFiles = Directory.GetFiles(AudioDirectory);

        var digits = new[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };

        foreach (var path in audioFiles)
        {
            if (Path.GetFileName(path).StartsWith('!')) continue;
            var name = Path.GetFileNameWithoutExtension(path).TrimEnd(digits).TrimEnd('_');

            var data = SoundMap.GetOrAdd(name);
            var didDecode = data.Add(File.ReadAllBytes(path));

            if (!didDecode)
            {
                Log.Error($"Failed to decode audio file at \"{path}\"");
            }
        }

        SoundGroups = new SoundGroup[Enum.GetNames(typeof(AudioGroup)).Length];
        SoundGroups[(int)AudioGroup.Master] = new() { Volume = Config.AudioMasterVolume };
        SoundGroups[(int)AudioGroup.Block] = new(parent: SoundGroups[0]) { Volume = Config.AudioBlockVolume };
        SoundGroups[(int)AudioGroup.Step] = new(parent: SoundGroups[0]) { Volume = Config.AudioStepVolume };
    }

    public static void Unload()
    {
        Foster.Audio.Audio.Shutdown();
    }

    public static void Update(Entity listener)
    {
        Foster.Audio.Audio.Listener.Direction = listener.Forward;
        ListenerPosition = listener.GetInterpolatedEyePosition(Time.FixedAlpha);
        Foster.Audio.Audio.Update();

        // Visualise audio instance position
        /* foreach (var instance in Foster.Audio.Audio.Instances)
        {
            Line.PushCube(ListenerPosition + instance.Position, Vector3.One * 0.1f, Color.Blue);
        } */
    }

    public static void Play(string name, float volume, EntityVector position, AudioGroup group = AudioGroup.Master)
    {
        if (name == "" || !SoundMap.TryGetValue(name, out SoundData? sound)) return;
        sound.Play(volume, group, position);
    }

    public static void Play(string name, float volume, AudioGroup group = AudioGroup.Master)
    {
        if (name == "" || !SoundMap.TryGetValue(name, out SoundData? sound)) return;
        sound.Play(volume, group);
    }
}