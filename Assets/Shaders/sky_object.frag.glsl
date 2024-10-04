#version 330

in vec2 v_tex;
in vec3 v_normal;

out vec4 frag_color;

uniform float u_alpha;
uniform vec4 u_color;
uniform vec3 u_selfDir;
uniform vec3 u_lightDir;
uniform sampler2D u_skyTexture;

#define PI 3.1415926535

void main(void) 
{
    float uv_y = acos(v_normal.z) / PI;
    float uv_x = atan(-v_normal.y, -v_normal.x) / (PI*2);

    vec3 col = texture(u_skyTexture, vec2(uv_x, uv_y)).rgb;

    float alpha = dot(normalize(v_normal), vec3(0.0, 0.0, 1.0));
    alpha = clamp(smoothstep(-0.05, 0.05, alpha), 0.0, 1.0);

    float radiance = clamp(dot(u_lightDir, -u_selfDir), 0.0, 1.0);

    col = mix(col, vec3(1.0), radiance * alpha);
    frag_color = vec4(col, 1.0) * u_alpha;
}