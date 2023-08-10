shader_type spatial;
render_mode unshaded;

uniform sampler2D _Texture : hint_default_white;

void fragment() {
	// Place fragment code here.
	ALBEDO = texture(_Texture, UV).rgb;
	ROUGHNESS = 1.0;
    SPECULAR = 0.0;
    METALLIC = 0.0;
}
