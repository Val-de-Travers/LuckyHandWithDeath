// Shader des dés 3D. Unlit, mais avec un ombrage calculé depuis la normale : le projet tourne
// sur le Renderer 2D d'URP, qui n'éclaire pas la 3D. Sans ce faux éclairage, les six faces
// auraient exactement la même luminosité et le cube se lirait comme une image plate.
//
// L'ombrage est calculé dans le shader, et non côté C# : le cube tourne à chaque changement de
// face, donc la luminosité doit suivre l'orientation RÉELLE de chaque face, pas sa place dans
// le cube. Autrement, la face amenée devant le joueur garderait la teinte sombre d'un côté.
//
// Pourquoi pas un shader tout fait :
//  · "Sprites/Default" attend un _RendererColor fourni par SpriteRenderer ; sur un MeshRenderer
//    il vaut zéro et tout devient noir.
//  · "Unlit/Transparent" n'a ni teinte ni ombrage.
//
// _ZWrite sert aux deux usages : corps du dé opaque (ZWrite 1, file Geometry) et vignettes de
// faces (ZWrite 0, file Transparent, occultées par le corps grâce au test de profondeur).
Shader "Dice3D/DieCubeUnlit"
{
    Properties
    {
        _MainTex   ("Texture", 2D) = "white" {}
        _Color     ("Tint", Color) = (1,1,1,1)
        _LightDir  ("Direction de la lumière", Vector) = (-0.3, 0.45, -0.85, 0)
        _ShadeMin  ("Luminosité de la face la plus sombre", Range(0,1)) = 0.55
        _ZWrite    ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }

        Cull Off
        Lighting Off
        ZTest LEqual
        ZWrite [_ZWrite]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float  shade : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;
            float4    _LightDir;
            float     _ShadeMin;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);

                // Demi-lambert : plus doux qu'un lambert sec, et aucune face ne tombe à zéro.
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float  d = dot(n, normalize(_LightDir.xyz)) * 0.5 + 0.5;
                o.shade  = lerp(_ShadeMin, 1.0, d);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                c.rgb *= i.shade;   // l'alpha n'est pas ombré : les coins arrondis restent nets
                return c;
            }
            ENDCG
        }
    }
}
