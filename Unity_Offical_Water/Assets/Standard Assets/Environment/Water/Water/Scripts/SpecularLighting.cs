using System;
using UnityEngine;

namespace UnityStandardAssets.Water
{
    /// <summary>
    /// Water4Simple
    ///     把传入的一个Transform的forward当成高光的方向。
    ///         public Transform specularLight
    /// </summary>
    [RequireComponent(typeof(WaterBase))]
    [ExecuteInEditMode]
    public class SpecularLighting : MonoBehaviour
    {
        public Transform specularLight;
        private WaterBase m_WaterBase;


        public void Start()
        {
            m_WaterBase = (WaterBase)gameObject.GetComponent(typeof(WaterBase));
        }


        public void Update()
        {
            if (!m_WaterBase)
            {
                m_WaterBase = (WaterBase)gameObject.GetComponent(typeof(WaterBase));
            }

            if (specularLight && m_WaterBase.sharedMaterial)
            {
                m_WaterBase.sharedMaterial.SetVector("_WorldLightDir", specularLight.transform.forward);
            }
        }
    }
}