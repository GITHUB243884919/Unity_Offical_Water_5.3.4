using System;
using UnityEngine;

namespace UnityStandardAssets.Water
{
    public enum WaterQuality
    {
        High = 2,
        Medium = 1,
        Low = 0,
    }

    /// <summary>
    /// 实现的是渲染配置
    ///     设置三档品质
    ///         通过sharedMaterial.shader.maximumLOD来配置。
    ///     是否使用EdgeBlend,并做相应的设置
    ///         判断当前系统是否支持深度纹理depth textures
    ///             SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Depth)
    ///         摄像机设置
    ///             Camera.main.depthTextureMode |= DepthTextureMode.Depth;
    ///             相机可以建立一个屏幕空间深度纹理。这个主要用于图像后期处理效果。注意，这个比较费。
    /// </summary>
    [ExecuteInEditMode]
    public class WaterBase : MonoBehaviour
    {
        public Material sharedMaterial;
        public WaterQuality waterQuality = WaterQuality.High;
        public bool edgeBlend = true;


        public void UpdateShader()
        {
            if (waterQuality > WaterQuality.Medium)
            {
                sharedMaterial.shader.maximumLOD = 501;
            }
            else if (waterQuality > WaterQuality.Low)
            {
                sharedMaterial.shader.maximumLOD = 301;
            }
            else
            {
                sharedMaterial.shader.maximumLOD = 201;
            }

            // If the system does not support depth textures (ie. NaCl), turn off edge bleeding,
            // as the shader will render everything as transparent if the depth texture is not valid.
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Depth))
            {
                edgeBlend = false;
            }

            if (edgeBlend)
            {
                Shader.EnableKeyword("WATER_EDGEBLEND_ON");
                Shader.DisableKeyword("WATER_EDGEBLEND_OFF");
                // just to make sure (some peeps might forget to add a water tile to the patches)
                if (Camera.main)
                {
                    Camera.main.depthTextureMode |= DepthTextureMode.Depth;
                }
            }
            else
            {
                Shader.EnableKeyword("WATER_EDGEBLEND_OFF");
                Shader.DisableKeyword("WATER_EDGEBLEND_ON");
            }
        }


        public void WaterTileBeingRendered(Transform tr, Camera currentCam)
        {
            if (currentCam && edgeBlend)
            {
                currentCam.depthTextureMode |= DepthTextureMode.Depth;
            }
        }


        public void Update()
        {
            if (sharedMaterial)
            {
                UpdateShader();
            }
        }
    }
}