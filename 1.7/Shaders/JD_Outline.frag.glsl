#version 450

// the game's Silhouette.frag.glsl with the same alpha cutoff as Cutout, so faint stray pixels in clothes and hair
// pictures (hidden in the normal draw) don't show in a marked pawn's outline

layout(location = 1) in vec2 vUv;

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color;      // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _MainTex_ST; // @default = vec4(0.0, 0.0, 1.0, 1.0)
} uMaterial;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;

void main()
{
    vec4 f = texture(_MainTex, vUv * uMaterial._MainTex_ST.zw + uMaterial._MainTex_ST.xy);
    if (f.a < 0.5)
        discard;
    outColor = vec4(uMaterial._Color.rgb, uMaterial._Color.a);
}
