using System;
using UnityEngine;

namespace UnityStandardAssets.Water
{
    /// <summary>
    /// Water4Simple
    ///     控制shader的WATER_VERTEX_DISPLACEMENT_ON和WATER_VERTEX_DISPLACEMENT_OFF
    ///     但目前好像没有用
    /// </summary>
    [ExecuteInEditMode]
    [RequireComponent(typeof(WaterBase))]
    public class GerstnerDisplace : Displace { }
}