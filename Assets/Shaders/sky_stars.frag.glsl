#version 330

in vec3 v_offset;
in vec3 v_position;
in vec2 v_tex;
in vec3 v_normal;

out vec4 o_FragColor;

uniform sampler2D u_skyTexture;

#define PI 3.1415926535

float luminance(vec3 col)
{
    return dot(col, vec3(0.2125, 0.7154, 0.0721));
}

void main(void) 
{
    float uv_y = acos(normalize(v_offset).z) / PI;
    float uv_x = atan(-normalize(v_offset).y, -normalize(v_offset).x) / (PI*2);
    vec4 sky = texture(u_skyTexture, vec2(uv_x, uv_y));

    float lumi = clamp(1.0 - pow(luminance(sky.rgb), 0.3) * 2, 0.0, 1.0);

    // Fade at horizon
    lumi *= clamp(dot(normalize(v_offset), vec3(0.0, 0.0, 1.0)) * 4, 0.0, 1.0);

    o_FragColor = vec4(vec3(lumi), 1.0);
}