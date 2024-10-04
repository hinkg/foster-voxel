using System.Numerics;

namespace Game;

static class VectorExt
{
    public static Vector2 XY(this Vector3 v) => new(v.X, v.Y);
    public static Vector3 XYZ(this Vector4 v) => new(v.X, v.Y, v.Z);
}

struct Vector2i : IFormattable
{
    public int X, Y;

    public Vector2i(int x, int y)
    {
        X = x;
        Y = y;
    }

    public readonly string ToString(string? format, IFormatProvider? formatProvider)
    {
        return $"[{X}, {Y}]";
    }

    public static Vector2i operator +(Vector2i a, Vector2i b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2i operator +(Vector2i a, (int X, int Y) b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2i operator +(Vector2i a, int b) => new(a.X + b, a.Y + b);

    public static Vector2i operator -(Vector2i a, Vector2i b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2i operator -(Vector2i a, (int X, int Y) b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2i operator -(Vector2i a, int b) => new(a.X - b, a.Y - b);

    public static bool operator ==(Vector2i a, Vector2i b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(Vector2i a, Vector2i b) => a.X != b.X || a.Y != b.Y;

    public readonly Vector2 ToVector2() => new(X, Y);

    public static Vector2i Zero => new Vector2i(0, 0);
}

struct Vector3i : IFormattable
{
    public int X, Y, Z;

    public readonly Vector2i XY => new(X, Y);

    public Vector3i(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public Vector3i(Vector3 v)
    {
        X = (int)v.X;
        Y = (int)v.Y;
        Z = (int)v.Z;
    }

    public Vector3i(Vector2i v, int z)
    {
        X = v.X;
        Y = v.Y;
        Z = z;
    }

    public readonly int GetLocalBlockIndex()
    {
        unchecked
        {
            return (X & 0x1F) + (Y & 0x1F) * Level.ChunkSize + (Z & 0x1F) * Level.ChunkArea;
        }
    }

    public readonly int GetNeighbourIndex()
    {
        unchecked
        {
            return (X + 1) + ((Y + 1) * 3) + (Z * 9);
        }
    }

    public readonly bool InsideChunk()
    {
        return X >= 0 && X < Level.ChunkSize && Y > 0 && Y < Level.ChunkSize && Z >= 0 && Z < Level.ChunkSize;
    }

    public readonly bool OutsideChunk()
    {
        return !InsideChunk();
    }

    public readonly Vector3i Wrap(out Vector3i diff)
    {
        var clamped = new Vector3i(
            X & 0x1F,
            Y & 0x1F,
            Z & 0x1F
        );

        diff.X = (X - clamped.X) >> 5;
        diff.Y = (Y - clamped.Y) >> 5;
        diff.Z = (Z - clamped.Z) >> 5;

        return clamped;
    }

    public readonly Vector3i Sign()
    {
        return new Vector3i(
            Math.Sign(X),
            Math.Sign(Y),
            Math.Sign(Z)
        );
    }

    public readonly Vector3i GetLocalBlock()
    {
        return new Vector3i(
            X & 0x1f,
            Y & 0x1f,
            Z & 0x1f
        );
    }

    public readonly Vector3i GetChunk()
    {
        return new Vector3i(
            X >> 5,
            Y >> 5,
            Z >> 5
        );
    }

    public readonly string ToString(string? format, IFormatProvider? formatProvider)
    {
        return $"[{X}, {Y}, {Z}]";
    }

    public readonly Vector3 ToVector3()
    {
        return new Vector3(X, Y, Z);
    }

    public static Vector3i operator +(Vector3i a, Vector3i b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3i operator +(Vector3i a, (int X, int Y, int Z) b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3i operator +(Vector3i a, int b) => new(a.X + b, a.Y + b, a.Z + b);

    public static Vector3i operator -(Vector3i a, Vector3i b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3i operator -(Vector3i a, (int X, int Y, int Z) b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3i operator -(Vector3i a, int b) => new(a.X - b, a.Y - b, a.Z - b);

    public static Vector3i operator *(Vector3i a, Vector3i b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    public static Vector3i operator *(Vector3i a, int b) => new(a.X * b, a.Y * b, a.Z * b);

    public static bool operator ==(Vector3i a, Vector3i b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    public static bool operator !=(Vector3i a, Vector3i b) => a.X != b.X || a.Y != b.Y || a.Z != b.Z;

    public static Vector3i UnitX => new(1, 0, 0);
    public static Vector3i UnitY => new(0, 1, 0);
    public static Vector3i UnitZ => new(0, 0, 1);

    public readonly Vector3i SelectAxis(int i) => this * (i == 0 ? UnitX : (i == 1 ? UnitY : UnitZ));
}

struct EntityAABB : IFormattable
{
    public EntityVector Position;
    public EntityVector Size;

    // Constructors

    public EntityAABB(long x, long y, long z, long w, long h, long d)
    {
        Position.X = x;
        Position.Y = y;
        Position.Z = z;
        Size.X = w;
        Size.Y = h;
        Size.Z = d;
    }

    public EntityAABB(EntityVector position, EntityVector size)
    {
        Position = position;
        Size = size;
    }

    public static EntityAABB FromBlock(Vector3i block)
    {
        return new EntityAABB(EntityVector.FromBlock(block), EntityVector.FromBlock(1, 1, 1));
    }

    public static EntityAABB FromBlock(int x, int y, int z)
    {
        return new EntityAABB(EntityVector.FromBlock(x, y, z), EntityVector.FromBlock(1, 1, 1));
    }

    public static EntityAABB FromChunk(Vector2i chunk)
    {
        return new EntityAABB(EntityVector.FromBlock((long)chunk.X * Level.ChunkSize, (long)chunk.Y * Level.ChunkSize, 0), EntityVector.FromBlock(Level.ChunkSize, Level.ChunkSize, Level.StackSizeZ));
    }

    public static EntityAABB FromEntity(EntityVector v, EntityVector size)
    {
        return new EntityAABB(v - new EntityVector(size.X / 2, size.Y / 2, 0), size);
    }

    //

    public readonly EntityVector Center => Position + Size / 2;

    // 

    public static bool IntersectList(EntityAABB a, Span<EntityAABB> b)
    {
        for (int i = 0; i < b.Length; i++)
        {
            if (Intersect(a, b[i])) return true;
        }

        return false;
    }

    public static bool IntersectList(EntityAABB a, Span<EntityAABB> b, out EntityAABB collider)
    {
        for (int i = 0; i < b.Length; i++)
        {
            if (Intersect(a, b[i]))
            {
                collider = b[i];
                return true;
            };
        }

        collider = default;
        return false;
    }

    public static bool Intersect(EntityAABB a, EntityAABB b)
    {
        return a.Position.X < b.Position.X + b.Size.X &&
               a.Position.X + a.Size.X > b.Position.X &&
               a.Position.Y < b.Position.Y + b.Size.Y &&
               a.Position.Y + a.Size.Y > b.Position.Y &&
               a.Position.Z < b.Position.Z + b.Size.Z &&
               a.Position.Z + a.Size.Z > b.Position.Z;
    }

    public static bool Intersect(EntityAABB aabb, EntityVector frustumCenter, Frustum frustum)
    {
        var localAABB = aabb;
        localAABB.Position = aabb.Position - frustumCenter;

        var low = localAABB.Position.ToVector3();
        var high = (localAABB.Position + localAABB.Size).ToVector3();

        for (int i = 0; i < 6; i++)
        {
            var a = new Vector3(
                frustum[i].X < 0.0 ? low.X : high.X,
                frustum[i].Y < 0.0 ? low.Y : high.Y,
                frustum[i].Z < 0.0 ? low.Z : high.Z
            );

            if (Vector3.Dot(frustum[i].XYZ(), a) + frustum[i].W < 0.0)
            {
                return false;
            }
        }

        return true;
    }

    //

    public readonly string ToString(string? format, IFormatProvider? formatProvider)
    {
        return $"[{Position}, {Size}]";
    }
}

struct EntityVector : IFormattable
{
    public long X, Y, Z;

    public long this[int index]
    {
        get
        {
            return index == 0 ? X : (index == 1 ? Y : Z);
        }
        set
        {
            if (index == 0) X = value;
            else if (index == 1) Y = value;
            else if (index == 2) Z = value;
        }
    }

    public EntityVector(long x, long y, long z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static EntityVector FromBlock(long x, long y, long z)
    {
        return new EntityVector(x << 27, y << 27, z << 27);
    }

    public static EntityVector FromBlock(Vector2i v)
    {
        return new EntityVector(((long)v.X) << 27, ((long)v.Y) << 27, 0);
    }

    public static EntityVector FromBlock(Vector3i v)
    {
        return new EntityVector(((long)v.X) << 27, ((long)v.Y) << 27, ((long)v.Z) << 27);
    }

    public static EntityVector FromBlock(float x, float y, float z)
    {
        return new EntityVector(
            FromBlock(x),
            FromBlock(y),
            FromBlock(z)
        );
    }

    public static EntityVector FromBlock(Vector3 v)
    {
        return new EntityVector(
            FromBlock(v.X),
            FromBlock(v.Y),
            FromBlock(v.Z)
        );
    }

    public readonly Vector3 ToVector3()
    {
        return new Vector3(
            ToBlockFloat(this.X),
            ToBlockFloat(this.Y),
            ToBlockFloat(this.Z)
        );
    }

    private readonly Vector3i GetFraction()
    {
        return new Vector3i(
            (int)((X) & ((1 << 27) - 1)),
            (int)((Y) & ((1 << 27) - 1)),
            (int)((Z) & ((1 << 27) - 1))
        );
    }

    public readonly Vector3i GetGlobalBlock()
    {
        return new Vector3i(
            (int)(X >> 27),
            (int)(Y >> 27),
            (int)(Z >> 27)
        );
    }

    public readonly Vector3i GetLocalBlock()
    {
        return new Vector3i(
            (int)((X >> 27) & 0x1f),
            (int)((Y >> 27) & 0x1f),
            (int)((Z >> 27) & 0x1f)
        );
    }

    public readonly Vector3i GetChunk()
    {
        return new Vector3i(
            (int)(X >> 32),
            (int)(Y >> 32),
            (int)(Z >> 32)
        );
    }

    public readonly int GetLocalBlockIndex()
    {
        unchecked
        {
            return (int)((X >> 27) & 0x1F) + (int)((Y >> 27) & 0x1F) * Level.ChunkSize + (int)((Z >> 27) & 0x1F) * Level.ChunkArea;
        }
    }

    public static EntityVector Sign(EntityVector v)
    {
        return new EntityVector(
            v.X < 0 ? -1 : 1,
            v.Y < 0 ? -1 : 1,
            v.Z < 0 ? -1 : 1
        );
    }

    public readonly long ManhattanLength => Math.Abs(X) + Math.Abs(Y) + Math.Abs(Z);

    public static EntityVector Floor(EntityVector v) => new(Floor(v.X), Floor(v.Y), Floor(v.Z));

    public static EntityVector Zero => FromBlock(0, 0, 0);
    public static EntityVector One => FromBlock(1, 1, 1);
    public static EntityVector UnitX => FromBlock(1, 0, 0);
    public static EntityVector UnitY => FromBlock(0, 1, 0);
    public static EntityVector UnitZ => FromBlock(0, 0, 1);

    public readonly EntityVector SelectAxis(int i) => this * (i == 0 ? UnitX : (i == 1 ? UnitY : UnitZ));

    public readonly long LargestComponentValue => Math.Max(Math.Max(X, Y), Z);

    //
    // Operators
    //

    public static EntityVector operator +(EntityVector a, EntityVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static EntityVector operator +(EntityVector a, Vector3i b) => a + FromBlock(b);

    public static EntityVector operator +(EntityVector a, Vector3 b) => a + FromBlock(b);

    public static EntityVector operator -(EntityVector a, EntityVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static EntityVector operator -(EntityVector a, Vector3i b) => a - FromBlock(b);

    public static EntityVector operator -(EntityVector a, Vector3 b) => a - FromBlock(b);

    public static bool operator ==(EntityVector a, EntityVector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    public static bool operator !=(EntityVector a, EntityVector b) => a.X != b.X || a.Y != b.Y || a.Z != b.Z;

    public static EntityVector operator *(EntityVector a, EntityVector b) => new(Mul(a.X, b.X), Mul(a.Y, b.Y), Mul(a.Z, b.Z));

    public static EntityVector operator *(EntityVector a, float b) => new(Mul(a.X, FromBlock(b)), Mul(a.Y, FromBlock(b)), Mul(a.Z, FromBlock(b)));

    public static EntityVector operator /(EntityVector a, long b) => new(a.X / b, a.Y / b, a.Z / b);


    //
    // Unit functions
    //

    public static long Floor(long axis) => ((axis >> 27) + 0) << 27;

    public static long Ceiling(long axis) => ((axis >> 27) + 1) << 27;

    public static long FromBlock(int a) => ((long)a) << 27;

    public static long FromBlock(long a) => ((long)a) << 27;

    public static long FromBlock(float a) => (((long)a) << 27) + (long)((a - Math.Truncate(a)) * (1 << 27));

    public static long FromBlock(double a) => (((long)a) << 27) + (long)((a - Math.Truncate(a)) * (1 << 27));

    public static int ToBlock(long a) => (int)(a >> 27);

    public static float ToBlockFloat(long a) => (float)(a >> 27) + ((float)(a & ((1 << 27) - 1)) / (1 << 27));

    public static double ToBlockDouble(long a) => (double)(a >> 27) + ((double)(a & ((1 << 27) - 1)) / (1 << 27));

    public static long Mul(long x, float y)
    {
        return Mul(x, FromBlock(y));
    }

    public static long Mul(long x, long y)
    {
        var xl = x;
        var yl = y;

        var xlo = (ulong)(xl & ((1 << 27) - 1));
        var xhi = xl >> 27;
        var ylo = (ulong)(yl & ((1 << 27) - 1));
        var yhi = yl >> 27;

        var lolo = xlo * ylo;
        var lohi = (long)xlo * yhi;
        var hilo = xhi * (long)ylo;
        var hihi = xhi * yhi;

        var loResult = lolo >> 27;
        var midResult1 = lohi;
        var midResult2 = hilo;
        var hiResult = hihi << 27;

        var sum = (long)loResult + midResult1 + midResult2 + hiResult;
        return sum;
    }

    //

    public readonly string ToString(string? format, IFormatProvider? formatProvider)
    {
        return $"{{{ToBlockDouble(X):0.00}, {ToBlockDouble(Y):0.00}, {ToBlockDouble(Z):0.00}}}";
    }
}
