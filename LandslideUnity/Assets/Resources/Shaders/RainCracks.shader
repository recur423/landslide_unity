Shader "Landslide/RainCracks"
{
    Properties
    {
        _Color("Crack color",Color)=(.048,.035,.024,1)
        _Opacity("Opening opacity",Range(0,1))=0
        _CrackTime("Seconds after rupture",Float)=0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float _Opacity,_CrackTime;
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos=UnityObjectToClipPos(v.vertex);
                o.uv=v.texcoord.xy;
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float reveal=smoothstep(i.uv.y,i.uv.y+.22,_CrackTime);
                float edge=1-smoothstep(.23,.50,abs(i.uv.x-.5));
                return fixed4(_Color.rgb,_Opacity*reveal*edge);
            }
            ENDCG
        }
    }
}
