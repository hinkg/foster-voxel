#version 330

uniform sampler2D u_skyTexture;

in vec2 v_tex;
in vec3 v_normal;
in vec4 v_color;

out vec4 frag_color;

#define PI 3.1415926535

void main(void) 
{
    float uv_y = acos(normalize(v_normal).z) / PI;
    float uv_x = atan(-normalize(v_normal).y, -normalize(v_normal).x) / (PI*2);

    vec4 skyColor = texture(u_skyTexture, vec2(uv_x, uv_y));
    frag_color = vec4(skyColor.rgb, 1.0);
}