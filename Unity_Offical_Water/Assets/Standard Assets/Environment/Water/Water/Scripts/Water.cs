using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityStandardAssets.Water
{
    [ExecuteInEditMode] // Make water live-update even when not in play mode
    public class Water : MonoBehaviour
    {
        public enum WaterMode
        {
            Simple = 0,
            Reflective = 1,
            Refractive = 2,
        };


        public WaterMode waterMode = WaterMode.Refractive;
        public bool disablePixelLights = true;
        public int textureSize = 256;
        public float clipPlaneOffset = 0.07f;
        public LayerMask reflectLayers = -1;
        public LayerMask refractLayers = -1;


        private Dictionary<Camera, Camera> m_ReflectionCameras = new Dictionary<Camera, Camera>(); // Camera -> Camera table
        private Dictionary<Camera, Camera> m_RefractionCameras = new Dictionary<Camera, Camera>(); // Camera -> Camera table
        private RenderTexture m_ReflectionTexture;
        private RenderTexture m_RefractionTexture;
        private WaterMode m_HardwareWaterSupport = WaterMode.Refractive;
        private int m_OldReflectionTextureSize;
        private int m_OldRefractionTextureSize;
        private static bool s_InsideWater;


        // This is called when it's known that the object will be rendered by some
        // camera. We render reflections / refractions and do other updates here.
        // Because the script executes in edit mode, reflections for the scene view
        // camera will just work!
        public void OnWillRenderObject()
        {
            Debug.Log("OnWillRenderObject " + Time.realtimeSinceStartup);
            if (!enabled || !GetComponent<Renderer>() || !GetComponent<Renderer>().sharedMaterial ||
                !GetComponent<Renderer>().enabled)
            {
                return;
            }

            // 这里为什么不是Camera.main而是Camera.current？
            // 因为Camera.main相当于我们的眼睛
            // 后面的camera是在处理反射相机和折射相机
            // 相当于眼睛长在水面上
            Camera cam = Camera.current;
            //Camera cam = Camera.main;
            if (!cam)
            {
                return;
            }

            // Safeguard from recursive water reflections.
            if (s_InsideWater)
            {
                return;
            }
            s_InsideWater = true;

            // Actual water rendering mode depends on both the current setting AND
            // the hardware support. There's no point in rendering refraction textures
            // if they won't be visible in the end.
            m_HardwareWaterSupport = FindHardwareWaterSupport();
            WaterMode mode = GetWaterMode();

            // 根据当前相机创建反射相机，纹理和折射相机，纹理
            // 相机: 带skybox组件和FlareLayer
            // 纹理：深度缓冲16bit
            Camera reflectionCamera, refractionCamera;
            CreateWaterObjects(cam, out reflectionCamera, out refractionCamera);

            // find out the reflection plane: position and normal in world space
            Vector3 pos = transform.position;
            Vector3 normal = transform.up;

            // Optionally disable pixel lights for reflection/refraction
            int oldPixelLightCount = QualitySettings.pixelLightCount;
            if (disablePixelLights)
            {
                QualitySettings.pixelLightCount = 0;
            }

            // 把当前相机的参数给反射相机和折射相机
            UpdateCameraModes(cam, reflectionCamera);
            UpdateCameraModes(cam, refractionCamera);

            // Render reflection if needed
            if (mode >= WaterMode.Reflective)
            {
                // Reflect camera around reflection plane
                // 获得反射纹理的几个流程
                //     1.反射摄像机的反射矩阵
                //     2.反射摄像机的投影矩阵
                //     3.反射摄像机x旋转下
                //     4.渲染到纹理
                // 通过构建发射平面进而构建反射矩阵，并将反射矩阵设置成反射摄像机的worldToCameraMatrix
                //     反射平面： 所在GameObject的法线（向上）和点（就选所在GameObject的位置）

                float d = -Vector3.Dot(normal, pos) - clipPlaneOffset;
                Vector4 reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);
                Matrix4x4 reflection = Matrix4x4.zero;
                CalculateReflectionMatrix(ref reflection, reflectionPlane);
                Vector3 oldpos = cam.transform.position;
                Vector3 newpos = reflection.MultiplyPoint(oldpos);
                reflectionCamera.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;

                // Setup oblique projection matrix so that near plane is our reflection
                // plane. This way we clip everything below/above it for free.
                // 投影矩阵
                // 参考http://www.cnblogs.com/wantnon/p/4569096.html
                //     也就是说，oblique投影矩阵与普通投影矩阵（透视投影矩阵和正交投影矩阵）的差别是：普通投影矩阵所描述的视截体近平面与锥轴垂直，
                //     而oblique投影矩阵所描述的视截体近平面是斜的（与锥轴不垂直）。
                //     由于水面是反射面，所以渲染反射图象时必须以视截体被水面所截的截面作为视口，即“斜视口”，
                //     所以必须将反射相机转化成oblique投影模式。
                //     reflectionCamera.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane)就是干这个事儿。
                Vector4 clipPlane = CameraSpacePlane(reflectionCamera, pos, normal, 1.0f);
                reflectionCamera.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane);

                reflectionCamera.cullingMask = ~(1 << 4) & reflectLayers.value; // never render water layer
                reflectionCamera.targetTexture = m_ReflectionTexture;
                bool oldCulling = GL.invertCulling;
				GL.invertCulling = !oldCulling;
                reflectionCamera.transform.position = newpos;
                Vector3 euler = cam.transform.eulerAngles;
                //反射是镜像，x转一下
                reflectionCamera.transform.eulerAngles = new Vector3(-euler.x, euler.y, euler.z);
                Debug.Log("Pre reflection.Camera.Render() Camera.current = " + Camera.current.gameObject.GetInstanceID() +
                    " main " + Camera.main.gameObject.GetInstanceID() + " " + Time.realtimeSinceStartup);
                reflectionCamera.Render();
                Debug.Log("Post reflection.Camera.Render()  Camera.current = " + Camera.current.gameObject.GetInstanceID() +
                    " main " + Camera.main.gameObject.GetInstanceID() + " " + Time.realtimeSinceStartup);
                reflectionCamera.transform.position = oldpos;
                GL.invertCulling = oldCulling;
                GetComponent<Renderer>().sharedMaterial.SetTexture("_ReflectionTex", m_ReflectionTexture);
            }

            // Render refraction
            if (mode >= WaterMode.Refractive)
            {
                refractionCamera.worldToCameraMatrix = cam.worldToCameraMatrix;

                // Setup oblique projection matrix so that near plane is our reflection
                // plane. This way we clip everything below/above it for free.
                Vector4 clipPlane = CameraSpacePlane(refractionCamera, pos, normal, -1.0f);
                refractionCamera.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane);

                refractionCamera.cullingMask = ~(1 << 4) & refractLayers.value; // never render water layer
                refractionCamera.targetTexture = m_RefractionTexture;
                refractionCamera.transform.position = cam.transform.position;
                refractionCamera.transform.rotation = cam.transform.rotation;
                Debug.Log("Pre refractionCamera.Render() Camera.current = " + Camera.current.gameObject.GetInstanceID() +
                    " main " + Camera.main.gameObject.GetInstanceID() + " " + Time.realtimeSinceStartup);
                refractionCamera.Render();
                Debug.Log("Post refractionCamera.Render() Camera.current = " + Camera.current.gameObject.GetInstanceID() +
                    " main " + Camera.main.gameObject.GetInstanceID() + " " + Time.realtimeSinceStartup);
                GetComponent<Renderer>().sharedMaterial.SetTexture("_RefractionTex", m_RefractionTexture);
            }

            // Restore pixel light count
            if (disablePixelLights)
            {
                QualitySettings.pixelLightCount = oldPixelLightCount;
            }

            // Setup shader keywords based on water mode
            switch (mode)
            {
                case WaterMode.Simple:
                    Shader.EnableKeyword("WATER_SIMPLE");
                    Shader.DisableKeyword("WATER_REFLECTIVE");
                    Shader.DisableKeyword("WATER_REFRACTIVE");
                    break;
                case WaterMode.Reflective:
                    Shader.DisableKeyword("WATER_SIMPLE");
                    Shader.EnableKeyword("WATER_REFLECTIVE");
                    Shader.DisableKeyword("WATER_REFRACTIVE");
                    break;
                case WaterMode.Refractive:
                    Shader.DisableKeyword("WATER_SIMPLE");
                    Shader.DisableKeyword("WATER_REFLECTIVE");
                    Shader.EnableKeyword("WATER_REFRACTIVE");
                    break;
            }

            s_InsideWater = false;
        }


        // Cleanup all the objects we possibly have created
        void OnDisable()
        {
            if (m_ReflectionTexture)
            {
                DestroyImmediate(m_ReflectionTexture);
                m_ReflectionTexture = null;
            }
            if (m_RefractionTexture)
            {
                DestroyImmediate(m_RefractionTexture);
                m_RefractionTexture = null;
            }
            foreach (var kvp in m_ReflectionCameras)
            {
                DestroyImmediate((kvp.Value).gameObject);
            }
            m_ReflectionCameras.Clear();
            foreach (var kvp in m_RefractionCameras)
            {
                DestroyImmediate((kvp.Value).gameObject);
            }
            m_RefractionCameras.Clear();
        }


        // This just sets up some matrices in the material; for really
        // old cards to make water texture scroll.
        void Update()
        {
            if (!GetComponent<Renderer>())
            {
                return;
            }
            Material mat = GetComponent<Renderer>().sharedMaterial;
            if (!mat)
            {
                return;
            }

            Vector4 waveSpeed = mat.GetVector("WaveSpeed");
            float waveScale = mat.GetFloat("_WaveScale");
            Vector4 waveScale4 = new Vector4(waveScale, waveScale, waveScale * 0.4f, waveScale * 0.45f);

            // Time since level load, and do intermediate calculations with doubles
            double t = Time.timeSinceLevelLoad / 20.0;
            Vector4 offsetClamped = new Vector4(
                (float)Math.IEEERemainder(waveSpeed.x * waveScale4.x * t, 1.0),
                (float)Math.IEEERemainder(waveSpeed.y * waveScale4.y * t, 1.0),
                (float)Math.IEEERemainder(waveSpeed.z * waveScale4.z * t, 1.0),
                (float)Math.IEEERemainder(waveSpeed.w * waveScale4.w * t, 1.0)
                );

            mat.SetVector("_WaveOffset", offsetClamped);
            mat.SetVector("_WaveScale4", waveScale4);
        }

        void UpdateCameraModes(Camera src, Camera dest)
        {
            if (dest == null)
            {
                return;
            }
            // set water camera to clear the same way as current camera
            dest.clearFlags = src.clearFlags;
            dest.backgroundColor = src.backgroundColor;
            if (src.clearFlags == CameraClearFlags.Skybox)
            {
                Skybox sky = src.GetComponent<Skybox>();
                Skybox mysky = dest.GetComponent<Skybox>();
                if (!sky || !sky.material)
                {
                    mysky.enabled = false;
                }
                else
                {
                    mysky.enabled = true;
                    mysky.material = sky.material;
                }
            }
            // update other values to match current camera.
            // even if we are supplying custom camera&projection matrices,
            // some of values are used elsewhere (e.g. skybox uses far plane)
            dest.farClipPlane = src.farClipPlane;
            dest.nearClipPlane = src.nearClipPlane;
            dest.orthographic = src.orthographic;
            dest.fieldOfView = src.fieldOfView;
            dest.aspect = src.aspect;
            dest.orthographicSize = src.orthographicSize;
        }


        // On-demand create any objects we need for water
        void CreateWaterObjects(Camera currentCamera, out Camera reflectionCamera, out Camera refractionCamera)
        {
            Debug.Log("CreateWaterObjects " + Time.realtimeSinceStartup);
            WaterMode mode = GetWaterMode();

            reflectionCamera = null;
            refractionCamera = null;

            if (mode >= WaterMode.Reflective)
            {
                // Reflection render texture
                if (!m_ReflectionTexture || m_OldReflectionTextureSize != textureSize)
                {
                    if (m_ReflectionTexture)
                    {
                        DestroyImmediate(m_ReflectionTexture);
                    }
                    m_ReflectionTexture = new RenderTexture(textureSize, textureSize, 16);
                    m_ReflectionTexture.name = "__WaterReflection" + GetInstanceID();
                    m_ReflectionTexture.isPowerOfTwo = true;
                    m_ReflectionTexture.hideFlags = HideFlags.DontSave;
                    m_OldReflectionTextureSize = textureSize;
                }

                // Camera for reflection
                m_ReflectionCameras.TryGetValue(currentCamera, out reflectionCamera);
                if (!reflectionCamera) // catch both not-in-dictionary and in-dictionary-but-deleted-GO
                {
                    // 创建一个带Camera和Skybox的GameObject
                    GameObject go = new GameObject("Water Refl Camera id" + GetInstanceID() + " for " + currentCamera.GetInstanceID(), typeof(Camera), typeof(Skybox));
                    reflectionCamera = go.GetComponent<Camera>();
                    reflectionCamera.enabled = false;
                    reflectionCamera.transform.position = transform.position;
                    reflectionCamera.transform.rotation = transform.rotation;
                    reflectionCamera.gameObject.AddComponent<FlareLayer>();
                    go.hideFlags = HideFlags.HideAndDontSave;
                    m_ReflectionCameras[currentCamera] = reflectionCamera;
                    Debug.Log("m_ReflectionCameras.Count = " + m_ReflectionCameras.Count + " "
                        + go.GetInstanceID() + " " 
                        + currentCamera.GetInstanceID() + " " + currentCamera.tag + " "
                        + GetInstanceID() + " "
                        + Time.realtimeSinceStartup);
                }
            }

            if (mode >= WaterMode.Refractive)
            {
                // Refraction render texture
                if (!m_RefractionTexture || m_OldRefractionTextureSize != textureSize)
                {
                    if (m_RefractionTexture)
                    {
                        DestroyImmediate(m_RefractionTexture);
                    }
                    m_RefractionTexture = new RenderTexture(textureSize, textureSize, 16);
                    m_RefractionTexture.name = "__WaterRefraction" + GetInstanceID();
                    m_RefractionTexture.isPowerOfTwo = true;
                    m_RefractionTexture.hideFlags = HideFlags.DontSave;
                    m_OldRefractionTextureSize = textureSize;
                }

                // Camera for refraction
                m_RefractionCameras.TryGetValue(currentCamera, out refractionCamera);
                if (!refractionCamera) // catch both not-in-dictionary and in-dictionary-but-deleted-GO
                {
                    GameObject go =
                        new GameObject("Water Refr Camera id" + GetInstanceID() + " for " + currentCamera.GetInstanceID(),
                            typeof(Camera), typeof(Skybox));
                    refractionCamera = go.GetComponent<Camera>();
                    refractionCamera.enabled = false;
                    refractionCamera.transform.position = transform.position;
                    refractionCamera.transform.rotation = transform.rotation;
                    refractionCamera.gameObject.AddComponent<FlareLayer>();
                    go.hideFlags = HideFlags.HideAndDontSave;
                    m_RefractionCameras[currentCamera] = refractionCamera;
                    Debug.Log("m_RefractionCameras.Count = " + m_RefractionCameras.Count + " "
                        + go.GetInstanceID() + " "
                        + currentCamera.GetInstanceID() + " " + currentCamera.tag + " "
                        + GetInstanceID() + " "
                        + Time.realtimeSinceStartup);
                }
            }
        }

        WaterMode GetWaterMode()
        {
            if (m_HardwareWaterSupport < waterMode)
            {
                return m_HardwareWaterSupport;
            }
            return waterMode;
        }

        WaterMode FindHardwareWaterSupport()
        {
            if (!SystemInfo.supportsRenderTextures || !GetComponent<Renderer>())
            {
                return WaterMode.Simple;
            }

            Material mat = GetComponent<Renderer>().sharedMaterial;
            if (!mat)
            {
                return WaterMode.Simple;
            }

            string mode = mat.GetTag("WATERMODE", false);
            if (mode == "Refractive")
            {
                return WaterMode.Refractive;
            }
            if (mode == "Reflective")
            {
                return WaterMode.Reflective;
            }

            return WaterMode.Simple;
        }

        // Given position/normal of the plane, calculates plane in camera space.
        Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
        {
            Vector3 offsetPos = pos + normal * clipPlaneOffset;
            Matrix4x4 m = cam.worldToCameraMatrix;
            Vector3 cpos = m.MultiplyPoint(offsetPos);
            Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
            return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
        }

        // Calculates reflection matrix around the given plane
        static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMat, Vector4 plane)
        {
            reflectionMat.m00 = (1F - 2F * plane[0] * plane[0]);
            reflectionMat.m01 = (- 2F * plane[0] * plane[1]);
            reflectionMat.m02 = (- 2F * plane[0] * plane[2]);
            reflectionMat.m03 = (- 2F * plane[3] * plane[0]);

            reflectionMat.m10 = (- 2F * plane[1] * plane[0]);
            reflectionMat.m11 = (1F - 2F * plane[1] * plane[1]);
            reflectionMat.m12 = (- 2F * plane[1] * plane[2]);
            reflectionMat.m13 = (- 2F * plane[3] * plane[1]);

            reflectionMat.m20 = (- 2F * plane[2] * plane[0]);
            reflectionMat.m21 = (- 2F * plane[2] * plane[1]);
            reflectionMat.m22 = (1F - 2F * plane[2] * plane[2]);
            reflectionMat.m23 = (- 2F * plane[3] * plane[2]);

            reflectionMat.m30 = 0F;
            reflectionMat.m31 = 0F;
            reflectionMat.m32 = 0F;
            reflectionMat.m33 = 1F;
        }
    }
}