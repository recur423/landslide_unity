Shader "Landslide/RainStreak"
{
    Properties { _MainTex("Particle",2D)="white"{} _TintColor("Tint",Color)=(.82,.9,1,.6) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; fixed4 _TintColor;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            v2f vert(appdata v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color*_TintColor;return o; }
            fixed4 frag(v2f i):SV_Target
            {
                fixed edge=1-smoothstep(.18,.5,abs(i.uv.x-.5));
                fixed end=smoothstep(0,.15,i.uv.y)*(1-smoothstep(.83,1,i.uv.y));
                return fixed4(i.color.rgb,i.color.a*edge*end);
            }
            ENDCG
        }
    }
}
