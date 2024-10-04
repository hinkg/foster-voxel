using Foster.Framework;

namespace Game;

public static class TexturePadding
{
    /// <summary>
    /// Generates a new atlas with a border around each tile. Border generated is equal to 
    /// the behaviour of CLAMP_TO_EDGE from OpenGL
    /// </summary>
    public static Image GeneratePaddedAtlas(this Image oldImage, int tileCountX, int tileCountY, bool consume = true)
    {
        var newImage = new Image(oldImage.Width * 2, oldImage.Height * 2, new Color(0xFF, 0x00, 0xFF, 0xFF));

        var oldData = oldImage.Data;
        var newData = newImage.Data;

        var oldTileWidth = oldImage.Width / tileCountX;
        var oldTileHeight = oldImage.Height / tileCountY;

        var newTileWidth = newImage.Width / tileCountX;
        var newTileHeight = newImage.Height / tileCountY;

        for (int y = 0; y < tileCountY; y++)
        {
            for (int x = 0; x < tileCountX; x++)
            {
                for (int v = 0; v < newTileHeight; v++)
                {
                    for (int u = 0; u < newTileWidth; u++)
                    {
                        int a = Math.Clamp(u - oldTileWidth / 2, 0, oldTileWidth - 1);
                        int b = Math.Clamp(v - oldTileHeight / 2, 0, oldTileHeight - 1);

                        int i = (x * newTileWidth + u) + (y * newTileHeight + v) * newImage.Width;
                        int j = (x * oldTileWidth + a) + (y * oldTileHeight + b) * oldImage.Width;

                        newData[i] = oldData[j];
                    }
                }
            }
        }

        if (consume) oldImage.Dispose();

        return newImage;
    }
}