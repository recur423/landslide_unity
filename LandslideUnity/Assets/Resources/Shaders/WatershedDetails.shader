Shader "Landslide/Watershed Details"
{
    Properties { _Water ("Rainwater", Float) = 0 }
    SubShader
    {
        Tags { "Queue"="Transparent-9" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Offset -1, -1
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            float _PresentationTime, _RainWetness, _RainStrength, _Water;
            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 world : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };
            Varyings vert(appdata_base input)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(input.vertex);
                o.uv = input.texcoord;
                o.world = mul(unity_ObjectToWorld,input.vertex).xyz;
                UNITY_TRANSFER_FOG(o,o.position);
                return o;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.world.xz;
                float spatial = sin(p.x*3.07 + sin(p.y*4.7))*sin(p.y*2.53 + sin(p.x*2.2));
                float edge = 1 - smoothstep(.16,.49,abs(input.uv.y-.5)+spatial*.075);
                edge *= smoothstep(0,.12,input.uv.x)*smoothstep(0,.09,1-input.uv.x);
                float damp = smoothstep(.14,.81,_RainWetness);
                float ripple = .5+.5*sin(p.x*7.1-p.y*5.2-7*_PresentationTime + 2*spatial);
                float flash = pow(ripple,6) * _RainStrength*.23;
                float3 wet = lerp(float3(.077,.077,.055), float3(.26,.30,.28), .29+flash);
                float3 clay = float3(.19,.148,.095) * (1+.2*spatial-.28*damp);
                half4 color = half4(lerp(clay,wet,_Water),edge*lerp(.61,damp*(.52+flash),_Water));
                UNITY_APPLY_FOG(input.fogCoord,color);
                return color;
            }
            ENDCG
        }
    }
}
