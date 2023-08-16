shader_type spatial;
render_mode unshaded;

uniform sampler2D _Texture : hint_default_white;
uniform bool depth = false;
uniform vec2 angles;
uniform vec2 offset;
uniform vec2 screenSize;
uniform float dist = 0.0f;
uniform float offsetMultiplier = 0.0f;
uniform float depthMultiplier = 0.005f;

const float TWO_PI = 6.28318530718;

vec3 LessThan(vec3 f, float value)
{
    return vec3(
        (f.x < value) ? 1.0f : 0.0f,
        (f.y < value) ? 1.0f : 0.0f,
        (f.z < value) ? 1.0f : 0.0f);
}
 
vec3 SRGBToLinear(vec3 rgb)
{
    rgb = clamp(rgb, 0.0f, 1.0f);
     
    return mix(
        pow(((rgb + 0.055f) / 1.055f), vec3(2.4f)),
        rgb / 12.92f,
        LessThan(rgb, 0.04045f)
    );
}

float GaussianBlurNoise(vec2 uv, float directions, float quality, float size)
{
    vec2 radius = size/screenSize;
	
    // Pixel colour
    float color = texture(_Texture, uv).a;
    
    // Blur calculations
    for( float d=0.0; d<TWO_PI; d+=TWO_PI/directions)
    {
		for(float i=1.0/quality; i<=1.0; i+=1.0/quality)
        {
			color += texture(_Texture, uv+vec2(cos(d),sin(d))*radius*i).a;		
        }
    }
    
    // Output to screen
    color /= quality * directions - 15.0;
    return color;
}

void fragment() {
	// Place fragment code here.
	vec2 uv = (vec2(-0.5f) + UV) / (1.0f + dist) + vec2(0.5f);
	if(depth) {
		// float depthValue = clamp(1.0f - texture(_Texture, UV).a, 0.0, 1.0);
		// ALBEDO = vec3(pow(depthValue, 2.25));
		float depthValue = clamp(1.0f - GaussianBlurNoise(uv, 8.0f, 3.0f, 24.0f), 0.0f, 1.0f);
		ALBEDO = vec3(depthValue);
	} else {
		float depthValue = GaussianBlurNoise(uv, 8.0f, 3.0f, 24.0f);
		
		vec2 uv = clamp(uv - offset * offsetMultiplier + angles * (depthValue + 0.5f) * dist * depthMultiplier, 0.0f, 1.0f);
		vec3 color = texture(_Texture, uv).rgb;
		ALBEDO = SRGBToLinear(color);
	}
	ROUGHNESS = 1.0f;
    SPECULAR = 0.0f;
    METALLIC = 0.0f;
}
