namespace Game;

public static class MathExt
{
    public static int IntLog2(int x)
    {
        for (int i = 0; i < 32; i++) if ((1 << i) >= x) return i;
        return 32;
    }

    public static int Mod(int a, int b) => (a % b + b) % b;

    public static long Mod(long a, long b) => (a % b + b) % b;
}

