#version 330

in vec3 v_color;

out vec4 frag_color;

void main(void) 
{
    frag_color = vec4(v_color, 1.0);
}