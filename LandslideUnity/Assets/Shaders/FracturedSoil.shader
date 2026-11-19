Shader "Landslide/FracturedSoil"
{
    Properties
    {
        _Color("Soil tint",Color)=(.9,.83,.72,1)
        _Crack("Crack width",Range(0,.2))=.055
        _EarthMap("Earth albedo / smoothness",2D)="white"{}
        _EarthNormal("Earth RGB normal",2D)="bump"{}
        _WorldScale("Tiles per metre",Range(.05,1))=.42
        _NormalStrength("Normal strength",Range(0,2))=.6
        _Wetness("Saturation from rainfall",Range(0,1))=.8
        _ExposureAge("Seconds since first rupture",Float)=0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows alpha:fade addshadow vertex:vert
        #pragma target 3.0
        #include "LandslideTriplanar.cginc"
        sampler2D _EarthMap,_EarthNormal;
        fixed4 _Color; float _Crack,_WorldScale,_NormalStrength,_Wetness,_ExposureAge;
        struct Input { float3 worldPos; float3 worldNormal; float4 color:COLOR; INTERNAL_DATA };
        void vert(inout appdata_full v,out Input o) { UNITY_INITIALIZE_OUTPUT(Input,o); o.color=v.color; }
        float2 hash2(float2 p) { return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3))))*43758.5453); }
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            float3 n=normalize(WorldNormalVector(IN,float3(0,0,1)));
            float3 w=TerrainBlendWeights(n);
            float4 earth=SampleTerrainTriplanar(_EarthMap,IN.worldPos,w,_WorldScale);
            float macro=SampleTerrainTriplanar(_EarthMap,IN.worldPos,w,.04).r;
            // Stretch cells along runout and warp their boundaries with the earth
            // texture. This reads as shear and flow rather than repeated brickwork.
            float2 warp=float2(macro-.5,tex2D(_EarthMap,IN.worldPos.xz*.031+float2(.37,.19)).g-.5);
            float2 p=IN.worldPos.xz*float2(.23,.43)+warp*.56;
            float2 cell=floor(p),f=frac(p);
            float first=10,second=10;
            for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++)
            {
                float2 v=float2(x,y)+hash2(cell+float2(x,y))-f;float d=dot(v,v);
                if(d<first){second=first;first=d;}else if(d<second)second=d;
            }
            float edge=smoothstep(0,_Crack,second-first);
            float flow=tex2D(_EarthMap,IN.worldPos.xz*float2(.018,.085)+float2(.43,.12)).r;
            float wet=saturate(_Wetness*lerp(.76f,1,IN.color.r));
            float fresh=1-smoothstep(1.6,10.5,_ExposureAge);
            float3 wetMud=earth.rgb*float3(3.0,2.8,2.55);
            float neutral=dot(wetMud,float3(.2126,.7152,.0722));
            wetMud=lerp(wetMud,neutral*float3(.95,.96,.90),fresh*.53);
            o.Albedo=wetMud*_Color.rgb*IN.color.rgb*lerp(.82,1,edge)*lerp(1,.75,wet)*lerp(.68,1,1-fresh)*lerp(.86,1.14,macro*2)*lerp(.88,1.08,flow*2);
            float3 detail=TerrainNormalDetail(_EarthNormal,IN.worldPos,w,_WorldScale);
            float3 t=normalize(WorldNormalVector(IN,float3(1,0,0)));
            float3 bitangent=normalize(WorldNormalVector(IN,float3(0,1,0)));
            o.Normal=TerrainTangentNormal(detail,n,t,bitangent,_NormalStrength);
            o.Smoothness=lerp(earth.a,lerp(.4,.49,fresh),wet)*lerp(.55,1,edge);
            o.Occlusion=lerp(.82,1,edge);o.Metallic=0;
            o.Alpha=IN.color.a;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
