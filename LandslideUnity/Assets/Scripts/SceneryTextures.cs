using System.Collections.Generic;
using UnityEngine;

namespace LandslideDemo
{
    /// <summary>Small shared, original textures; generated once, never in Update.</summary>
    public static class SceneryTextures
    {
        const int Size=256;
        sealed class Surface { public Texture2D Albedo,Normal; }
        static readonly Dictionary<string,Surface> surfaces=new Dictionary<string,Surface>();
        static Texture2D leafCluster;
        public static Texture2D LeafCluster
        {
            get
            {
                if(leafCluster!=null)return leafCluster;
                var pixels=new Color[Size*Size];
                for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
                {
                    Vector2 p=new Vector2((x+.5f)/Size,(y+.5f)/Size);float coverage=0,vein=0;
                    for(int i=0;i<7;i++)
                    {
                        float cy=.20f+i*.095f,cx=.5f+(i%2==0?-.155f:.155f),angle=i%2==0?-.58f:.58f;
                        Vector2 d=p-new Vector2(cx,cy);float u=d.x*Mathf.Cos(angle)-d.y*Mathf.Sin(angle),v=d.x*Mathf.Sin(angle)+d.y*Mathf.Cos(angle);
                        float radius=Mathf.Sqrt(u*u/(.205f*.205f)+v*v/(.075f*.075f));
                        coverage=Mathf.Max(coverage,Mathf.Clamp01((1-radius)*30));
                        if(radius<1)vein=Mathf.Max(vein,Mathf.Exp(-Mathf.Abs(v)*190)*.12f);
                    }
                    float stem=Mathf.Clamp01((.009f-Mathf.Abs(p.x-(.48f+.04f*p.y)))*400)*Mathf.Clamp01((p.y-.09f)*40)*Mathf.Clamp01((.87f-p.y)*40);
                    float n=Noise(p.x*8,p.y*8,8),value=.67f+.27f*n+vein;
                    pixels[x+y*Size]=new Color(value*.93f,value,value*.70f,Mathf.Max(coverage,stem));
                }
                leafCluster=Texture("Original compound leaf spray",pixels,false,true);return leafCluster;
            }
        }

        public static void Apply(Material material,string name)
        {
            string key=name.ToLowerInvariant(),kind=null;
            if(key.Contains("bark"))kind="bark";
            else if(key.Contains("roof"))kind="roof";
            else if(key.Contains("timber"))kind="timber";
            else if(key.Contains("plaster"))kind="plaster";
            else if(key.Contains("glazing"))kind="glass";
            else if(key.Contains("foundation"))kind="concrete";
            if(kind==null)return;
            Surface surface;
            if(!surfaces.TryGetValue(kind,out surface)||surface.Albedo==null)
            {surface=Generate(kind);surfaces[kind]=surface;}
            material.mainTexture=surface.Albedo;
            if(material.HasProperty("_BumpMap"))
            {material.SetTexture("_BumpMap",surface.Normal);material.SetFloat("_BumpScale",kind=="roof"?.55f:.28f);material.EnableKeyword("_NORMALMAP");}
            if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",kind=="glass"?.76f:(kind=="roof"?.17f:.08f));
            if(kind=="glass"&&material.HasProperty("_Metallic"))material.SetFloat("_Metallic",.18f);
        }

        static Surface Generate(string kind)
        {
            var colors=new Color[Size*Size];var heights=new float[Size*Size];
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
            {
                float u=x/(float)Size,v=y/(float)Size,n=Noise(u*16,v*16,16),fine=Hash(x,y),h=n*.18f,value=.84f+n*.22f+fine*.045f;
                if(kind=="bark"||kind=="timber")
                {
                    float grain=Mathf.Sin((u*17+Noise(u*8,v*2,8)*.65f)*Mathf.PI*2);
                    float groove=Mathf.Pow(Mathf.Clamp01(grain),8);value=.72f+.30f*n-.29f*groove;h=n*.14f-groove*.28f;
                    if(kind=="bark"){float crack=Noise(u*32,v*8,32);value*=.82f+crack*.25f;}
                }
                else if(kind=="roof")
                {
                    float row=Mathf.Floor(v*5),tu=Mathf.Repeat(u*6+(row%2)*.5f,1),tv=Mathf.Repeat(v*5,1);
                    float seam=Mathf.Min(Mathf.Min(tu,1-tu)*2,Mathf.Min(tv,1-tv)*1.7f);
                    float grout=1-Edge(0,.065f,seam),curve=Mathf.Sin(tu*Mathf.PI);
                    value=(.70f+curve*.21f+n*.16f)*(1-grout*.58f);h=curve*.40f+tv*.14f-grout*.35f;
                }
                else if(kind=="plaster")
                {float stain=Noise(u*4,v*4,4);value=.82f+stain*.19f+fine*.05f;h=n*.16f+fine*.07f;}
                else if(kind=="concrete")
                {value=.72f+n*.30f+fine*.10f;h=n*.26f+fine*.12f;}
                else if(kind=="glass")
                {
                    float edge=Mathf.Min(Mathf.Min(u,1-u),Mathf.Min(v,1-v));
                    float frame=1-Edge(.035f,.060f,edge);
                    float bar=1-Edge(.013f,.030f,Mathf.Min(Mathf.Abs(u-.5f),Mathf.Abs(v-.5f)));
                    value=(.80f+.21f*v+.12f*Mathf.Exp(-Mathf.Pow((u+v-1.2f)*8,2)))*(1-Mathf.Max(frame,bar)*.70f);h=0;
                }
                colors[x+y*Size]=new Color(value,value,value,1);heights[x+y*Size]=h;
            }
            var normals=new Color[Size*Size];
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
            {
                float dx=heights[((x+1)%Size)+y*Size]-heights[((x+Size-1)%Size)+y*Size];
                float dy=heights[x+((y+1)%Size)*Size]-heights[x+((y+Size-1)%Size)*Size];
                Vector3 normal=new Vector3(-dx*3,-dy*3,1).normalized;float nx=normal.x*.5f+.5f,ny=normal.y*.5f+.5f;
                // RG + opaque alpha supports both RGB and Unity's RG-or-AG unpack.
                normals[x+y*Size]=new Color(nx,ny,normal.z*.5f+.5f,1);
            }
            return new Surface{Albedo=Texture("Original "+kind+" albedo",colors,false,false),Normal=Texture("Original "+kind+" normal",normals,true,false)};
        }
        static Texture2D Texture(string name,Color[] pixels,bool linear,bool clamp)
        {
            var texture=new Texture2D(Size,Size,TextureFormat.RGBA32,true,linear){name=name,wrapMode=clamp?TextureWrapMode.Clamp:TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=4};
            texture.SetPixels(pixels);texture.Apply(true,false);
            if(clamp)
            {
                // Preserve the alpha-test silhouette in distant canopy mip levels.
                // Plain averaged alpha makes small leaf sprays vanish in wide shots.
                float coverage=0;foreach(Color pixel in pixels)if(pixel.a>.42f)coverage++;
                coverage/=pixels.Length;
                for(int level=1;level<texture.mipmapCount;level++)
                {
                    Color[] mip=texture.GetPixels(level);float low=.5f,high=8;
                    for(int iteration=0;iteration<14;iteration++)
                    {
                        float middle=(low+high)*.5f,count=0;foreach(Color p in mip)if(p.a*middle>.42f)count++;
                        if(count/mip.Length<coverage)low=middle;else high=middle;
                    }
                    for(int i=0;i<mip.Length;i++)mip[i].a=Mathf.Clamp01(mip[i].a*high);
                    texture.SetPixels(mip,level);
                }
            }
            texture.Apply(false,true);return texture;
        }
        static float Hash(int x,int y)
        {unchecked{uint n=(uint)(x*374761393+y*668265263);n=(n^(n>>13))*1274126177;return (n&0xffffff)/16777215f;}}
        // Mathf.SmoothStep interpolates endpoints; normalize thresholds explicitly.
        static float Edge(float from,float to,float value)
        {return Mathf.SmoothStep(0,1,Mathf.InverseLerp(from,to,value));}
        static float Noise(float x,float y,int period)
        {
            int ix=Mathf.FloorToInt(x),iy=Mathf.FloorToInt(y);float fx=x-ix,fy=y-iy;fx=fx*fx*(3-2*fx);fy=fy*fy*(3-2*fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix%period,iy%period),Hash((ix+1)%period,iy%period),fx),Mathf.Lerp(Hash(ix%period,(iy+1)%period),Hash((ix+1)%period,(iy+1)%period),fx),fy);
        }
    }
}
