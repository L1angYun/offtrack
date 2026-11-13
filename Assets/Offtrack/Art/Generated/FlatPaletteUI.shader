Shader "Offtrack/FlatPaletteUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Font Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        // Edges and glyphs blend between palette colors (anti-aliasing); fills stay opaque palette colors.
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Tests enable this global keyword to render hard palette-exact edges and prove that
            // only Palette's fixed colors and single dimmer composites are drawn.
            #pragma multi_compile __ OFFTRACK_STRICT_PALETTE
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            sampler2D _MainTex;
            fixed4 _TextureSampleAdd;
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed coverage = (tex2D(_MainTex, i.uv) + _TextureSampleAdd).a;
            #ifdef OFFTRACK_STRICT_PALETTE
                clip(coverage - 0.5);
                return i.color;
            #else
                return fixed4(i.color.rgb, coverage * i.color.a);
            #endif
            }
            ENDCG
        }
    }
}
