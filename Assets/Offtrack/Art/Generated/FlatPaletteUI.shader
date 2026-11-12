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
            // nothing but the four palette colors is drawn; players get the blended edges below.
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
                fixed alpha = (tex2D(_MainTex, i.uv) + _TextureSampleAdd).a * i.color.a;
            #ifdef OFFTRACK_STRICT_PALETTE
                clip(alpha - 0.5);
                return fixed4(i.color.rgb, 1);
            #else
                return fixed4(i.color.rgb, alpha);
            #endif
            }
            ENDCG
        }
    }
}
