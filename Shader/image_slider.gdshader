shader_type canvas_item;

uniform float t: hint_range(0.0, 1.0, 0.001) = 0.5;
uniform bool depth = false;

void fragment() {
	if(depth) {
		float depthValue = (1.0 - COLOR.a);
		COLOR = vec4(depthValue, depthValue, depthValue, float(UV.x > t));
	}
	else {
		COLOR = vec4(COLOR.xyz, float(UV.x > t));
	}
}
