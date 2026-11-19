Shader "Landslide/OvercastSky"
{
    Properties { _Zenith("Zenith",Color)=(.31,.36,.39,1) _Horizon("Horizon",Color)=(.57,.61,.61,1) }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Zenith,_Horizon;
            struct v2f { float4 pos:SV_POSITION; float3 dir:TEXCOORD0; };
            v2f vert(float4 vertex:POSITION) { v2f o;o.pos=UnityObjectToClipPos(vertex);o.dir=vertex.xyz;return o; }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p) { float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y); }
            float4 frag(v2f i):SV_Target
            {
                float3 d=normalize(i.dir);float h=saturate(d.y);
                float3 col=lerp(_Horizon.rgb,_Zenith.rgb,pow(h,.6));
                float2 p=d.xz/max(.15,d.y+.22)*1.6;
                float cloud=noise(p)*.55+noise(p*2.03+4)*.29+noise(p*4.07)*.16;
                col+=smoothstep(.36,.76,cloud)*.07*smoothstep(0,.18,h);
                col-=smoothstep(.32,.68,noise(p*.68+11))*.025*smoothstep(0,.2,h);
                return float4(col,1);
            }
            ENDCG
        }
    }
}
