Shader "Landslide/VertexTerrain"
{
    Properties
    {
        _Color("Tint",Color)=(1,1,1,1)
        _GrassMap("Grass albedo / smoothness",2D)="white"{}
        _EarthMap("Soil albedo / smoothness",2D)="white"{}
        _RockMap("Rock albedo / smoothness",2D)="white"{}
        _GrassNormal("Grass RGB normal",2D)="bump"{}
        _EarthNormal("Soil RGB normal",2D)="bump"{}
        _RockNormal("Rock RGB normal",2D)="bump"{}
        _WorldScale("Tiles per metre",Range(.05,1))=.23
        _NormalStrength("Normal strength",Range(0,2))=.35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        #include "LandslideTriplanar.cginc"
        sampler2D _GrassMap,_EarthMap,_RockMap,_GrassNormal,_EarthNormal,_RockNormal;
        fixed4 _Color;
        float _WorldScale,_NormalStrength,_RainWetness;
        struct Input { float3 worldPos; float3 worldNormal; float4 color:COLOR; float2 groundCover; INTERNAL_DATA };
        void vert(inout appdata_full v,out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input,o);
            o.color=v.color;
            o.groundCover=v.texcoord1.xy;
        }
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            float3 n=normalize(WorldNormalVector(IN,float3(0,0,1)));
            float3 w=TerrainBlendWeights(n);
            float distant=saturate(IN.groundCover.x);
            float macro=TerrainMacroNoise(IN.worldPos.xz);
            float farPatch=TerrainMacroNoise(IN.worldPos.xz*.34+float2(731,127));
            float farRock=smoothstep(.15,.57,1-saturate(n.y))*.61;
            float farCover=saturate(macro*.37+farPatch*.63);
            float3 farGrass=lerp(float3(.095,.125,.087),float3(.205,.235,.17),farCover);
            float3 farAlbedo=lerp(farGrass,float3(.235,.23,.207),farRock);
            if(distant>.995)
            {
                o.Albedo=farAlbedo;o.Normal=float3(0,0,1);o.Metallic=0;o.Smoothness=.035;o.Occlusion=1;
                return;
            }
            float exposed=saturate(IN.color.a);
            float slope=1-saturate(n.y);
            float meso=TerrainMacroNoise(IN.worldPos.xz*3.2+float2(46,226));
            float channel=saturate(IN.groundCover.y)*lerp(.82,1.05,meso);
            float strata=saturate(-IN.groundCover.y);
            float brokenRock=lerp(.27,.88,smoothstep(.25,.77,meso));
            float slopeRock=smoothstep(.19,.57,slope+meso*.04)*brokenRock*(1-.61*channel);
            float rock=max(strata*.92,slopeRock)*lerp(1,.55,exposed);
            float lowland=1-smoothstep(40,145,IN.worldPos.y);
            float ravineSoil=channel*smoothstep(.31,.76,meso)*.17;
            float earth=saturate(exposed+lowland*.11+smoothstep(.12,.34,slope)*.26+ravineSoil)*(1-rock);
            float grass=saturate(1-rock-earth);
            float4 a=SampleTerrainTriplanar(_GrassMap,IN.worldPos,w,_WorldScale);
            float4 b=SampleTerrainTriplanar(_EarthMap,IN.worldPos,w,_WorldScale);
            float4 c=SampleTerrainTriplanar(_RockMap,IN.worldPos,w,_WorldScale*.7);
            // Leafy Grass contains dry leaf litter; correct its photographed base
            // toward the existing green hillside without flattening the detail.
            float3 grassPhoto=a.rgb*lerp(float3(.56,.77,.63),float3(.53,.80,.70),meso);
            float3 rockPhoto=lerp(c.rgb,dot(c.rgb,float3(.299,.587,.114))*float3(.97,.98,1.03),.52)*.78;
            float3 baseColor=(grassPhoto*grass+b.rgb*earth+rockPhoto*rock)*IN.color.rgb*_Color.rgb;
            float3 macroColor=lerp(float3(.11,.17,.105),float3(.215,.30,.19),macro)*grass
                +lerp(float3(.25,.19,.13),float3(.42,.32,.20),macro)*earth
                +lerp(float3(.26,.28,.26),float3(.37,.39,.36),macro)*rock;
            float cameraDistance=distance(_WorldSpaceCameraPos,IN.worldPos);
            float detailFade=smoothstep(120,420,cameraDistance);
            o.Albedo=lerp(baseColor,macroColor,detailFade*.77);
            o.Albedo*=lerp(.81,1.13,macro*.73+meso*.27);
            o.Albedo*=lerp(float3(1,1,1),float3(.59,.71,.68),channel);
            float shade=1-saturate(dot(n,normalize(_WorldSpaceLightPos0.xyz)));
            o.Albedo*=lerp(float3(1,1,1),float3(.87,.96,1.04),shade*.35);
            float3 detail=TerrainNormalDetail(_GrassNormal,IN.worldPos,w,_WorldScale)*grass
                +TerrainNormalDetail(_EarthNormal,IN.worldPos,w,_WorldScale)*earth
                +TerrainNormalDetail(_RockNormal,IN.worldPos,w,_WorldScale*.7)*rock;
            float3 t=normalize(WorldNormalVector(IN,float3(1,0,0)));
            float3 bitangent=normalize(WorldNormalVector(IN,float3(0,1,0)));
            o.Normal=TerrainTangentNormal(detail,n,t,bitangent,_NormalStrength*(1-detailFade*.87));
            o.Metallic=0;
            o.Smoothness=a.a*grass+b.a*earth+c.a*rock;
            // Rain changes appearance only; infiltration and safety factor remain solver data.
            float wet=saturate(_RainWetness);
            o.Albedo*=lerp(1,.77,wet);
            o.Smoothness=lerp(o.Smoothness,saturate(o.Smoothness+.09+.19*channel),wet);
            o.Occlusion=lerp(lerp(.94,1,a.r),.82,channel*.67);
            o.Albedo=lerp(o.Albedo,farAlbedo,distant);
            o.Normal=normalize(lerp(o.Normal,float3(0,0,1),distant));
            o.Smoothness=lerp(o.Smoothness,.035,distant);
            o.Occlusion=lerp(o.Occlusion,1,distant);
        }
        ENDCG
    }
    Fallback "Diffuse"
}
