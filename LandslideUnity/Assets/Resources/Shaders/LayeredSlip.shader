Shader "Landslide/LayeredSlip"
{
    Properties
    {
        _Color("Layer tint",Color)=(1,1,1,1)
        _EarthMap("Earth albedo / smoothness",2D)="white"{}
        _RockMap("Rock albedo / smoothness",2D)="white"{}
        _EarthNormal("Earth RGB normal",2D)="bump"{}
        _RockNormal("Rock RGB normal",2D)="bump"{}
        _WorldScale("Tiles per metre",Range(.05,1))=.33
        _NormalStrength("Normal strength",Range(0,2))=.4
        _Wetness("Rain sheen",Range(0,1))=.55
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        #include "Assets/Shaders/LandslideTriplanar.cginc"
        sampler2D _EarthMap,_RockMap,_EarthNormal,_RockNormal;
        fixed4 _Color;
        float _WorldScale,_NormalStrength,_Wetness;
        struct Input { float3 worldPos; float3 worldNormal; float4 color:COLOR; INTERNAL_DATA };
        void vert(inout appdata_full v,out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input,o);
            o.color=v.color;
        }
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            float3 worldNormal=normalize(WorldNormalVector(IN,float3(0,0,1)));
            float3 weights=TerrainBlendWeights(worldNormal);
            float4 earth=SampleTerrainTriplanar(_EarthMap,IN.worldPos,weights,_WorldScale);
            float4 rock=SampleTerrainTriplanar(_RockMap,IN.worldPos,weights,_WorldScale*.78);
            // Vertex alpha denotes exposed strata (0 = weathered earth, 1 =
            // bedrock); the mesh and colors are presentation geometry only.
            float rockWeight=smoothstep(.05,.95,saturate(IN.color.a));
            float3 earthColor=earth.rgb*float3(2.4,2.22,2.02);
            float3 rockColor=rock.rgb*float3(1.75,1.72,1.67);
            float macro=TerrainMacroNoise(IN.worldPos.xz*1.4);
            o.Albedo=lerp(earthColor,rockColor,rockWeight)*IN.color.rgb*_Color.rgb;
            o.Albedo*=lerp(.91,1.07,macro)*lerp(1,.84,saturate(_Wetness));
            float3 earthDetail=TerrainNormalDetail(_EarthNormal,IN.worldPos,weights,_WorldScale);
            float3 rockDetail=TerrainNormalDetail(_RockNormal,IN.worldPos,weights,_WorldScale*.78);
            float3 detail=lerp(earthDetail,rockDetail,rockWeight);
            float3 tangent=normalize(WorldNormalVector(IN,float3(1,0,0)));
            float3 bitangent=normalize(WorldNormalVector(IN,float3(0,1,0)));
            o.Normal=TerrainTangentNormal(detail,worldNormal,tangent,bitangent,_NormalStrength);
            o.Metallic=0;
            o.Smoothness=saturate(lerp(earth.a,rock.a,rockWeight)*lerp(1,1.18,saturate(_Wetness)));
            o.Occlusion=.93;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
