shader_type canvas_item;

uniform float t: hint_range(0.0, 1.0, 0.001) = 0.5;

void fragment() {
	COLOR = vec4(COLOR.xyz, float(UV.x > t));
}
