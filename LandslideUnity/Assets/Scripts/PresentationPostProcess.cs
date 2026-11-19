using UnityEngine;

namespace LandslideDemo
{
    [RequireComponent(typeof(Camera))]
    public sealed class PresentationPostProcess : MonoBehaviour
    {
        Material material;
        void OnEnable()
        {
            var shader=Resources.Load<Shader>("Shaders/PresentationGrade");
            if(shader==null || !shader.isSupported) { enabled=false; return; }
            material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            GetComponent<Camera>().depthTextureMode |= DepthTextureMode.DepthNormals;
        }
        void OnRenderImage(RenderTexture source,RenderTexture destination)
        {
            if(material==null){Graphics.Blit(source,destination);return;}
            var bloom=RenderTexture.GetTemporary(Mathf.Max(1,source.width/4),Mathf.Max(1,source.height/4),0,source.format);
            var blur=RenderTexture.GetTemporary(bloom.width,bloom.height,0,source.format);
            Graphics.Blit(source,bloom,material,0);
            material.SetVector("_BlurDirection",new Vector4(1f/bloom.width,0,0,0));Graphics.Blit(bloom,blur,material,1);
            material.SetVector("_BlurDirection",new Vector4(0,1f/bloom.height,0,0));Graphics.Blit(blur,bloom,material,1);
            material.SetTexture("_BloomTex",bloom);Graphics.Blit(source,destination,material,2);
            RenderTexture.ReleaseTemporary(blur);RenderTexture.ReleaseTemporary(bloom);
        }
        void OnDisable(){if(material!=null)Destroy(material);}
    }
}
