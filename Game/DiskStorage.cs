using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Game;

// Compression tiers 
//     8 bit => 2048 byte palette =>  8 blocks / word => 4096 words => 32768 bytes / chunk
//     7 bit => 1024 byte palette =>  9 blocks / word => 3641 words => 29128 bytes / chunk
//     6 bit =>  512 byte palette => 10 blocks / word => 3277 words => 26216 bytes / chunk
//     5 bit =>  256 byte palette => 12 blocks / word => 2731 words => 21848 bytes / chunk
//     4 bit =>  128 byte palette => 16 blocks / word => 2048 words => 16384 bytes / chunk
//     3 bit =>   64 byte palette => 21 blocks / word => 1561 words => 12488 bytes / chunk
//     2 bit =>   32 byte palette => 32 blocks / word => 1024 words =>  8192 bytes / chunk
//     1 bit =>   16 byte palette => 64 blocks / word =>  512 words =>  4096 bytes / chunk

//
// This file needs major cleanup 
//

using CompressionDataWord = UInt64;

static class PaletteCompression
{
    public static int IntLog2(int x)
    {
        for (int i = 0; i < 32; i++) if ((1 << i) >= x) return i;
        return 32;
    }

    public static int WordCount(int paletteCount)
    {
        int bitWidth = Math.Max(IntLog2(paletteCount), 1);
        int entriesPerWord = (sizeof(CompressionDataWord) * 8) / bitWidth;
        return Level.ChunkVolume / entriesPerWord + ((sizeof(CompressionDataWord) * 8) % entriesPerWord != 0 ? 1 : 0);
    }

    public static void Decompress(Span<Block> chunk, in Span<Block> palette, in Span<CompressionDataWord> compressed)
    {
        int currentWord = 0;
        int currentBit = 0;
        int bitWidth = Math.Max(IntLog2(palette.Length), 1);

        for (int i = 0; i < Level.ChunkVolume; i++)
        {
            int bits = (int)(compressed[currentWord] >> currentBit) & ((1 << bitWidth) - 1);
            chunk[i] = palette[bits];

            currentBit += bitWidth;

            if ((currentBit + bitWidth) > (sizeof(CompressionDataWord) * 8))
            {
                currentBit = 0;
                currentWord += 1;
            }
        }
    }

    public static void Compress(in Span<Block> chunk, ref Block[] palette, out int paletteCount, ref CompressionDataWord[] compressed, out int wordCount)
    {
        paletteCount = 0;

        var distinct = new byte[256];
        for (int i = 0; i < distinct.Length; i++) distinct[i] = byte.MaxValue;
        for (int i = 0; i < Level.ChunkVolume; i++) distinct[(int)chunk[i]] = 0;

        for (int i = 0; i < distinct.Length; i++)
        {
            if (distinct[i] == byte.MaxValue) continue;
            palette[paletteCount] = (Block)i;

            distinct[i] = (byte)paletteCount;
            paletteCount += 1;
        }

        int currentWord = 0;
        int currentBit = 0;
        int bitWidth = Math.Max(IntLog2(paletteCount), 1);

        for (int i = 0; i < Level.ChunkVolume; i++)
        {
            int v = distinct[(int)chunk[i]];

            if (currentBit == 0) compressed[currentWord] = 0;
            compressed[currentWord] |= ((ulong)v) << currentBit;

            currentBit += bitWidth;

            if ((currentBit + bitWidth) > (sizeof(CompressionDataWord) * 8))
            {
                currentBit = 0;
                currentWord += 1;
            }
        }

        if (currentBit != 0) currentWord++;
        wordCount = currentWord;

        //Console.WriteLine($"Compressed to {bitWidth} bit width, {paletteCount} palette entries, {currentWord} words, {(currentWord)*8} bytes");
        //Console.Write("Palette: ");
        //for (int i = 0; i < paletteCount; i++) Console.Write($"{i} = {palette[i]}, ");
        //Console.Write("\n");
    }
}

class Region
{
    public struct ChunkData
    {
        public int Offset;
        public int Size;
        public int CompressionMode;
        public int CompressionTier;
        public readonly int Next => Offset + Size;

        public ChunkData(int offset, int size, int compressionMode, int compressionTier)
        {
            Offset = offset;
            Size = size;
            CompressionMode = compressionMode;
            CompressionTier = compressionTier;
        }

        public static void Serialize(in Vector3i position, in ChunkData data, ref List<byte> bytes)
        {
            bytes.AddRange(BitConverter.GetBytes(position.X));
            bytes.AddRange(BitConverter.GetBytes(position.Y));
            bytes.AddRange(BitConverter.GetBytes(position.Z));
            bytes.AddRange(BitConverter.GetBytes(data.Offset));
            bytes.AddRange(BitConverter.GetBytes(data.Size));
            bytes.AddRange(BitConverter.GetBytes(data.CompressionMode));
            bytes.AddRange(BitConverter.GetBytes(data.CompressionTier));
        }

        public static void Deserialize(in Span<byte> bytes, out Vector3i position, out ChunkData chunkData)
        {
            position = new(
                BitConverter.ToInt32(bytes.Slice(00, 4)),
                BitConverter.ToInt32(bytes.Slice(04, 4)),
                BitConverter.ToInt32(bytes.Slice(08, 4))
            );

            chunkData = new(
                BitConverter.ToInt32(bytes.Slice(12, 4)),
                BitConverter.ToInt32(bytes.Slice(16, 4)),
                BitConverter.ToInt32(bytes.Slice(20, 4)),
                BitConverter.ToInt32(bytes.Slice(24, 4))
            );
        }
    }

    // Maps global stack position (NOT the position relative to this region) to offset within
    // data byte array
    public Dictionary<Vector3i, ChunkData> chunkIndex;
    byte[] data;

    public Region()
    {
        chunkIndex = [];
        data = [];
    }

    public bool ContainsStack(Vector2i stackPosition)
    {
        for (int i = 0; i < Level.StackChunkCount; i++)
        {
            if (ContainsChunk(new Vector3i(stackPosition, i))) return true;
        }

        return false;
    }

    public bool ContainsChunk(Vector3i chunkPosition)
    {
        return chunkIndex.ContainsKey(chunkPosition);
    }

    public bool GetChunkSafe(Vector3i chunkPosition, out Span<byte> bytes, out ChunkData chunkData)
    {
        bytes = null;
        var gotValue = chunkIndex.TryGetValue(chunkPosition, out chunkData);
        if (gotValue) bytes = data.AsSpan(chunkData.Offset, chunkData.Size);
        return gotValue;
    }

    public void Insert(Vector3i chunkPosition, int compressionMode, int compressionTier, ReadOnlySpan<byte> inData)
    {
        bool gotValue = chunkIndex.TryGetValue(chunkPosition, out ChunkData chunkData);

        if (!gotValue)
        {
            chunkData = new ChunkData(data.Length, inData.Length, compressionMode, compressionTier);
        }

        bool isLast = !gotValue || ((chunkData.Offset + chunkData.Size) == data.Length);

        int sizeDelta = inData.Length - chunkData.Size;
        bool shouldResize = !gotValue || sizeDelta != 0;

        chunkData.Size = inData.Length;
        chunkData.CompressionMode = compressionMode;
        chunkData.CompressionTier = compressionTier;
        chunkIndex[chunkPosition] = chunkData;

        if (shouldResize)
        {
            var newData = new byte[this.data.Length + (gotValue ? 0 : chunkData.Size) + sizeDelta];

            // Copy data before
            if (chunkData.Offset != 0)
            {
                this.data.AsSpan(0, chunkData.Offset).CopyTo(newData.AsSpan(0, chunkData.Offset));
            }

            // Copy data after
            if (!isLast)
            {
                var start = chunkData.Next - sizeDelta;
                var length = this.data.Length - start;
                this.data.AsSpan(start: start).CopyTo(newData.AsSpan(start: chunkData.Next));

                foreach (var (pos, toMoveData) in chunkIndex)
                {
                    if (toMoveData.Offset > chunkData.Offset)
                    {
                        var d = toMoveData;
                        d.Offset += sizeDelta;
                        chunkIndex[pos] = d;
                    }
                }
            }

            this.data = newData;
        }

        inData.CopyTo(this.data.AsSpan(chunkData.Offset, chunkData.Size));
    }

    public static Region ReadFromDisk(string path)
    {
        var region = new Region();
        var fileData = File.ReadAllBytes(path);

        // Read chunk count
        int chunkIndexCount = BitConverter.ToInt32(fileData.AsSpan(0, sizeof(Int32)));

        // Size of chunk data
        var entrySize = sizeof(Int32) * (3 + 4);

        // Read chunk entries
        for (int i = 0; i < chunkIndexCount; i++)
        {
            var entrySpan = fileData.AsSpan(sizeof(Int32) * 1 + i * entrySize, entrySize);
            ChunkData.Deserialize(entrySpan, out var chunkPosition, out var chunkData);
            region.chunkIndex[chunkPosition] = chunkData;
        }

        // Read data
        int dataOffset = (sizeof(Int32) * 1 + chunkIndexCount * entrySize);
        int dataSize = fileData.Length - dataOffset;
        region.data = new byte[dataSize];
        fileData.AsSpan(dataOffset).CopyTo(region.data);

        return region;
    }

    public bool WriteToDisk(string path)
    {
        // There's probably an IFormatter or something to use instead of this mess
        List<byte> bytes = [];

        // Push chunk count
        bytes.AddRange(BitConverter.GetBytes(chunkIndex.Count));

        // Push chunk entries
        foreach (var (pos, chunkData) in chunkIndex)
        {
            ChunkData.Serialize(pos, chunkData, ref bytes);
        }

        // Push data
        bytes.AddRange(data);

        // Write to disk
        try
        {
            File.WriteAllBytes(path, bytes.ToArray());
        }
        catch (Exception e)
        {
            Log.Error($"Failed to write region file at \"{path}\" ({e})");
            return false;
        }

        return true;
    }
}

class DiskStorage
{
    public const uint Version = 1;
    public const string MetadataFileName = "Metadata";
    public const string RegionIndexFileName = "Index";
    public static string RegionFileName(int index) => $"Region-{index}";

    public static string MakeSavesDirectoryPath()
    {
        var exePath = Path.GetDirectoryName(AppContext.BaseDirectory) ?? "./";
        var path = Path.Join(exePath, "Saves");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }

    public string DataDirectory = "";
    public SaveMetaData MetaData;

    Dictionary<Vector2i, int> regionIndex;
    Dictionary<Vector2i, Region> loadedRegions;
    int regionIndexCounter = 0;

    public class SaveMetaData
    {
        public uint Version;
        public string DisplayName;
        public DateTime CreationDate;
        public int Seed;
        public int ElapsedTicks;
        public int ElapsedDayCycleTicks;

        public SaveMetaData(string displayName)
        {
            Version = DiskStorage.Version;
            DisplayName = displayName;
            CreationDate = DateTime.Now;
        }

        private static readonly JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static SaveMetaData? Deserialize(string source)
        {
            return JsonSerializer.Deserialize<SaveMetaData>(source, jsonOptions);
        }

        public string Serialize()
        {
            return JsonSerializer.Serialize(this, jsonOptions);
        }

        public bool Validate()
        {
            return this.Version == DiskStorage.Version;
        }
    }

    public DiskStorage()
    {
        regionIndex = [];
        loadedRegions = [];
    }

    public static (SaveMetaData data, string path)[] ScanForSaves()
    {
        var dirNames = Directory.GetDirectories(MakeSavesDirectoryPath());

        List<(SaveMetaData data, string path)> saveList = [];

        foreach (var dir in dirNames)
        {
            string metaDataPath = Path.Join(dir, MetadataFileName);
            if (!File.Exists(metaDataPath)) continue;

            string? jsonText;

            try
            {
                jsonText = File.ReadAllText(metaDataPath);
            }
            catch (Exception e)
            {
                Log.Error($"Failed to read metadata file at \"{metaDataPath}\" ({e.Message})");
                continue;
            }

            var data = SaveMetaData.Deserialize(jsonText);

            if (data == null || !data.Validate())
            {
                Log.Error($"Invalid metadata file at \"{metaDataPath}\"");
                continue;
            }

            saveList.Add((data, dir));
        }

        return saveList.ToArray();
    }

    public static bool CreateSave(string DisplayName)
    {
        var saveDirectoryName = new string(Array.FindAll(DisplayName.ToLower().Replace(' ', '-').ToCharArray(), c => char.IsAsciiLetterOrDigit(c)));
        var saveDirectoryPath = Path.Join(MakeSavesDirectoryPath(), saveDirectoryName);

        if (File.Exists(saveDirectoryPath)) return false;

        var metaData = new SaveMetaData(DisplayName)
        {
            Seed = Random.Shared.Next(),
            ElapsedDayCycleTicks = Sky.SunOrbitPeriod / 24 * 2,
        };

        try
        {
            Directory.CreateDirectory(saveDirectoryPath);
            File.WriteAllText(Path.Join(saveDirectoryPath, MetadataFileName), metaData.Serialize());
        }
        catch (Exception e)
        {
            Log.Error($"Failed to create save ({e.Message})");
            return false;
        }

        return true;
    }

    public bool LoadSave(string saveDirectoryPath)
    {
        DataDirectory = saveDirectoryPath;

        string metaDataPath = Path.Join(DataDirectory, MetadataFileName);
        if (!File.Exists(metaDataPath))
        {
            Log.Error($"Couldn't find metadata file at \"{metaDataPath}\"");
            return false;
        }

        string? jsonText;

        try
        {
            jsonText = File.ReadAllText(metaDataPath);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to read metadata file at \"{metaDataPath}\" ({e.Message})");
            return false;
        }

        SaveMetaData? data = SaveMetaData.Deserialize(jsonText);

        if (data == null || !data.Validate())
        {
            Log.Error($"Invalid metadata file at \"{metaDataPath}\"");
            return false;
        }

        this.MetaData = data;

        LoadRegionIndex();

        Data.IntializeDataMaps(
            blockMapPath: Path.Join(DataDirectory, "BlockMap"),
            itemMapPath: Path.Join(DataDirectory, "ItemMap")
        );

        Data.ParseJsonEntries("Assets/blocks.json");
        Data.ParseJsonData("Assets/blocks.json");
        Data.ParseJsonData("Assets/biomes.json");
        Data.MakeItemBlockData();
        Data.MakeEntityData();

        Log.Info("Initialized disk storage");
        Log.Trace($"Name: \"{MetaData.DisplayName}\"");
        Log.Trace($"Save Location: \"{DataDirectory}\"");
        if (regionIndex.Count > 0) Log.Trace($"Found {regionIndex.Count} region(s)");

        return true;
    }

    void LoadRegionIndex()
    {
        byte[] data;

        try
        {
            var regionPath = Path.Join(DataDirectory, RegionIndexFileName);
            data = File.ReadAllBytes(regionPath);
        }
        catch (Exception e)
        {
            Log.Warn($"Couldn't load region index ({e.Message})");
            return;
        }

        regionIndexCounter = BitConverter.ToInt32(data.AsSpan(0, sizeof(Int32)));

        for (int i = sizeof(Int32); i < data.Length; i += 3 * sizeof(Int32))
        {
            var entry = new Vector3i(
                BitConverter.ToInt32(data.AsSpan(i + 0 * sizeof(Int32), 4)),
                BitConverter.ToInt32(data.AsSpan(i + 1 * sizeof(Int32), 4)),
                BitConverter.ToInt32(data.AsSpan(i + 2 * sizeof(Int32), 4))
            );

            regionIndex[entry.XY] = entry.Z;
        }
    }

    void WriteRegionIndex()
    {
        List<byte> data = [];

        data.AddRange(BitConverter.GetBytes(regionIndexCounter));

        foreach (var (position, index) in regionIndex)
        {
            data.AddRange(BitConverter.GetBytes(position.X));
            data.AddRange(BitConverter.GetBytes(position.Y));
            data.AddRange(BitConverter.GetBytes(index));
        }

        try
        {
            var regionPath = Path.Join(DataDirectory, RegionIndexFileName);
            File.WriteAllBytes(regionPath, data.ToArray());
        }
        catch (Exception e)
        {
            Log.Error($"Couldn't write region index ({e.Message})");
            return;
        }
    }

    Region CreateRegion(Vector2i regionPosition)
    {
        var region = new Region();
        regionIndex[regionPosition] = regionIndexCounter;
        regionIndexCounter += 1;
        return region;
    }

    Region LoadRegion(Vector2i regionPosition)
    {
        int index = regionIndex[regionPosition];
        var regionPath = Path.Join(DataDirectory, RegionFileName(index));
        var region = Region.ReadFromDisk(regionPath);
        loadedRegions[regionPosition] = region;
        return region;
    }

    Region GetRegion(Vector2i regionPosition)
    {
        if (loadedRegions.TryGetValue(regionPosition, out Region? value))
        {
            return value;
        }
        else
        {
            return LoadRegion(regionPosition);
        }
    }

    Region GetOrCreateRegion(Vector2i regionPosition)
    {
        if (regionIndex.ContainsKey(regionPosition))
        {
            if (loadedRegions.TryGetValue(regionPosition, out Region? value))
            {
                return value;
            }
            else
            {
                return LoadRegion(regionPosition);
            }
        }
        else
        {
            return CreateRegion(regionPosition);
        }
    }

    public void LoadStack(Vector2i stackPosition, ref ChunkStack stack, out int loadedChunkCount)
    {
        var regionPosition = new Vector2i(
            stackPosition.X >> 4,
            stackPosition.Y >> 4
        );

        loadedChunkCount = 0;

        if (!regionIndex.ContainsKey(regionPosition)) return;
        var region = GetRegion(regionPosition);
        if (!region.ContainsStack(stackPosition)) return;

        for (int i = 0; i < Level.StackChunkCount; i++)
        {
            bool foundChunk = region.GetChunkSafe(new Vector3i(stackPosition, i), out var bytes, out var chunkData);
            if (!foundChunk) continue;

            int paletteCount = chunkData.CompressionTier;
            int paletteCapacity = 1 << Math.Max(PaletteCompression.IntLog2(paletteCount), 1);

            int wordCount = (bytes.Length - (paletteCapacity * sizeof(Block))) / sizeof(CompressionDataWord);

            var palette = MemoryMarshal.Cast<byte, Block>(bytes.Slice(0, paletteCount * sizeof(Block)));
            var words = MemoryMarshal.Cast<byte, CompressionDataWord>(bytes.Slice(paletteCapacity * sizeof(Block), wordCount * sizeof(CompressionDataWord)));

            PaletteCompression.Decompress(stack.Chunks[i].Span, palette, words);

            stack.IsLoadedFromDisk[i] = true;
            loadedChunkCount++;
        }
    }

    public void WriteSave(Level level)
    {
        var sw = Stopwatch.StartNew();

        if (!Directory.Exists(DataDirectory))
        {
            bool success = CreateSave(MetaData.DisplayName);
            if (!success) return; // todo: error handling
        }

        MetaData.ElapsedTicks = level.CurrentTick;
        MetaData.ElapsedDayCycleTicks = level.CurrentDayCycleTick;
        File.WriteAllText(Path.Join(DataDirectory, MetadataFileName), this.MetaData.Serialize());

        // Gather list of dirty chunks
        Dictionary<Vector2i, Stack<Vector3i>> regionStacks = [];

        foreach (var (stackPosition, chunkStack) in level.StackMap)
        {
            for (int i = 0; i < chunkStack.IsDirty.Length; i++)
            {
                if (!chunkStack.IsDirty[i]) continue;

                var regionPosition = new Vector2i(stackPosition.X >> 4, stackPosition.Y >> 4);
                var regionStack = regionStacks.GetOrAdd(regionPosition);

                regionStack.Push(new Vector3i(stackPosition, i));
            }
        }

        var compressionWorkingBuffer = new CompressionDataWord[(Level.ChunkVolume * sizeof(Block)) / sizeof(CompressionDataWord)];
        var paletteWorkingBuffer = new Block[256];
        var copyBuffer = new byte[paletteWorkingBuffer.Length * sizeof(Block) + compressionWorkingBuffer.Length * sizeof(CompressionDataWord)];

        foreach (var (regionPos, regionStack) in regionStacks)
        {
            var region = GetOrCreateRegion(regionPos);

            while (regionStack.Count > 0)
            {
                var chunkPosition = regionStack.Pop();
                Span<Block> blocks = level.GetStack(chunkPosition.XY).Chunks[chunkPosition.Z].Span;
                PaletteCompression.Compress(blocks, ref paletteWorkingBuffer, out int paletteCount, ref compressionWorkingBuffer, out int wordCount);

                int paletteCapacity = 1 << Math.Max(PaletteCompression.IntLog2(paletteCount), 1);

                var paletteBytes = MemoryMarshal.AsBytes(paletteWorkingBuffer.AsSpan(0, paletteCapacity));
                var wordBytes = MemoryMarshal.AsBytes(compressionWorkingBuffer.AsSpan(0, wordCount));

                paletteBytes.CopyTo(copyBuffer.AsSpan(0, paletteBytes.Length));

                wordBytes.CopyTo(copyBuffer.AsSpan(paletteBytes.Length, wordBytes.Length));

                region.Insert(chunkPosition, 1, paletteCount, copyBuffer.AsSpan(0, paletteBytes.Length + wordBytes.Length));
            }

            var regionPath = Path.Join(DataDirectory, RegionFileName(regionIndex[regionPos]));
            region.WriteToDisk(regionPath);
        }

        WriteRegionIndex();

        Data.WriteDataMaps(
            blockMapPath: Path.Join(DataDirectory, "BlockMap"),
            itemMapPath: Path.Join(DataDirectory, "ItemMap")
        );

        Log.Info($"Saved level \"{MetaData.DisplayName}\" to disk in {sw.Elapsed.TotalMilliseconds:0.00}ms");
    }
}