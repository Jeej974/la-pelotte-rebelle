shader_type canvas_item;

uniform float blur_strength : hint_range(0.0, 10.0) = 0.5;
uniform sampler2D screen_texture : hint_screen_texture;

void fragment() {
    vec2 pixel = SCREEN_PIXEL_SIZE;
    vec4 sum = vec4(0.0);
    float total_weight = 0.0;
    int radius = 2;

    for (int x = -radius; x <= radius; x++) {
        for (int y = -radius; y <= radius; y++) {
            vec2 offset = vec2(float(x), float(y)) * pixel * blur_strength * 0.75;
            float weight = exp(-(float(x * x + y * y)) / 8.0);
            sum += texture(screen_texture, SCREEN_UV + offset) * weight;
            total_weight += weight;
        }
    }

    COLOR = sum / total_weight;
}
