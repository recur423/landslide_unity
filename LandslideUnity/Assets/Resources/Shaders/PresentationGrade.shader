Shader "Hidden/Landslide/PresentationGrade"
{
    Properties
    {
        _MainTex("Source",2D)="white"{}
        _BloomTex("Bloom",2D)="black"{}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex,_BloomTex,_CameraDepthNormalsTexture;
        float4 _MainTex_TexelSize,_BlurDirection;
        float _PresentationTime,_RainStrength;
        float3 aces(float3 x) { return saturate((x*(2.51*x+.03))/(x*(2.43*x+.59)+.14)); }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i):SV_Target {float3 c=tex2D(_MainTex,i.uv).rgb;float l=max(c.r,max(c.g,c.b));return float4(c*saturate((l-.85)/max(l,.0001)),1);}
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i):SV_Target {float2 d=_BlurDirection.xy;return tex2D(_MainTex,i.uv)*.4+(tex2D(_MainTex,i.uv+d*1.5)+tex2D(_MainTex,i.uv-d*1.5))*.24+(tex2D(_MainTex,i.uv+d*3.5)+tex2D(_MainTex,i.uv-d*3.5))*.06;}
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i):SV_Target
            {
                float3 c=tex2D(_MainTex,i.uv).rgb;float depth;float3 normal;
                DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture,i.uv),depth,normal);
                float occlusion=0;
                const float2 taps[8]={float2(1,0),float2(-1,0),float2(0,1),float2(0,-1),float2(.7,.7),float2(-.7,.7),float2(.7,-.7),float2(-.7,-.7)};
                for(int k=0;k<8;k++)
                {
                    float z;float3 n;DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture,i.uv+taps[k]*_MainTex_TexelSize.xy*5),z,n);
                    float delta=depth-z;
                    occlusion+=smoothstep(.0003,.002,delta)*(1-smoothstep(.004,.018,delta))*saturate(1-dot(normal,n)+.25);
                }
                c*=1-saturate(occlusion/8)*.26;
                c+=tex2D(_BloomTex,i.uv).rgb*.075;
                c=aces(c*1.26);
                // Sparse lens-plane streaks make distant rainfall legible in an overview.
                // Their clock follows replay time, so seeking and frame capture are stable.
                float2 rainUv=i.uv*float2(430,215);
                float stripe=rainUv.x+rainUv.y*.19;
                float lane=floor(stripe);
                float choice=frac(sin(lane*43.17)*43758.5453);
                float slip=frac(rainUv.y*.29+choice+_PresentationTime*4.1);
                float streak=(1-smoothstep(.055,.18,abs(frac(stripe)-.5)))
                    *smoothstep(.05,.2,slip)*(1-smoothstep(.56,.82,slip))
                    *step(.85,choice);
                c+=streak*_RainStrength*float3(.075,.085,.09);
                float l=dot(c,float3(.2126,.7152,.0722));c=lerp(l.xxx,c,.91);
                c*=float3(1.0,1.01,1.02);
                float2 q=(i.uv-.5)*float2(1.3,1);c*=1-.16*smoothstep(.18,.7,dot(q,q));
                return float4(c,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
