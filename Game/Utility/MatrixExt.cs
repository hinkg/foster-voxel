using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game;

[InlineArray(6)]
struct Frustum
{
    Vector4 plane;
}

static class MatrixExt
{
    public static Frustum GetFrustum(this Matrix4x4 matrix)
    {
        Frustum frustum = default;

        // Left
        frustum[0][0] = matrix[0, 3] + matrix[0, 0];
        frustum[0][1] = matrix[1, 3] + matrix[1, 0];
        frustum[0][2] = matrix[2, 3] + matrix[2, 0];
        frustum[0][3] = matrix[3, 3] + matrix[3, 0];

        // Right
        frustum[1][0] = matrix[0, 3] - matrix[0, 0];
        frustum[1][1] = matrix[1, 3] - matrix[1, 0];
        frustum[1][2] = matrix[2, 3] - matrix[2, 0];
        frustum[1][3] = matrix[3, 3] - matrix[3, 0];

        // Top
        frustum[2][0] = matrix[0, 3] - matrix[0, 1];
        frustum[2][1] = matrix[1, 3] - matrix[1, 1];
        frustum[2][2] = matrix[2, 3] - matrix[2, 1];
        frustum[2][3] = matrix[3, 3] - matrix[3, 1];

        // Bottom
        frustum[3][0] = matrix[0, 3] + matrix[0, 1];
        frustum[3][1] = matrix[1, 3] + matrix[1, 1];
        frustum[3][2] = matrix[2, 3] + matrix[2, 1];
        frustum[3][3] = matrix[3, 3] + matrix[3, 1];

        // Near
        frustum[4][0] = matrix[0, 3];
        frustum[4][1] = matrix[1, 3];
        frustum[4][2] = matrix[2, 3];
        frustum[4][3] = matrix[3, 3];

        // Far
        frustum[5][0] = matrix[0, 3] - matrix[0, 3];
        frustum[5][1] = matrix[1, 3] - matrix[1, 3];
        frustum[5][2] = matrix[2, 3] - matrix[2, 3];
        frustum[5][3] = matrix[3, 3] - matrix[3, 3];

        for (int i = 0; i < 6; i++)
        {
            var len = frustum[i].XYZ().Length();
            frustum[i] /= len;
        }

        return frustum;
    }
}