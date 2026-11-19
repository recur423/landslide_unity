Shader "Landslide/Leaf Cards"
{
    Properties
    {
        _MainTex ("Compound leaf alpha", 2D) = "white" {}
        _Color ("Leaf tint", Color) = (0.35,0.45,0.2,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.42
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        LOD 200
        Cull Off
        AlphaToMask On
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows alphatest:_Cutoff vertex:vert addshadow
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        float _PresentationTime;
        struct Input { float2 uv_MainTex; float3 worldPos; };
        void vert(inout appdata_full v)
        {
            // Playback clock, not _Time: pauses, seeks and captures stay deterministic.
            float weight=saturate(v.vertex.y*.18);
            float phase=dot(v.vertex.xyz,float3(.51,.37,.79));
            v.vertex.x+=sin(_PresentationTime*1.5+phase)*.075*weight;
            v.vertex.z+=sin(_PresentationTime*1.1+phase*1.3)*.045*weight;
        }
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            fixed4 tex=tex2D(_MainTex,IN.uv_MainTex);
            o.Albedo=tex.rgb*_Color.rgb;
            // Modest transmitted sky light keeps two-sided leaves readable in shadow.
            o.Emission=o.Albedo*.08;
            o.Metallic=0;o.Smoothness=.15;o.Occlusion=.88;o.Alpha=tex.a;
        }
        ENDCG
    }
    FallBack "Transparent/Cutout/Diffuse"
}
