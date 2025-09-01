using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace XRMultiplayer
{
    /// <summary>
    /// Simple utility that toggles Shadow Casting on static renderers before a bake
    /// and toggles it off upon bake completion.
    /// </summary>
    public class LightBakeUtility : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField, Tooltip("Renderers assigned here will enable shadows before light baking and disable shadows upon light bake completion.")]
        Renderer[] m_StaticRenderers;

        [SerializeField, Tooltip("Renderers assigned here will not have their shadow settings changed by this tool during the light baking process.")]
        Renderer[] m_Filters;

        [SerializeField, Tooltip("Transforms assigned here will gather all children Renderers and will enable and disable shadow during the light baking process.")]
         Transform[] m_RendererParents;

        [SerializeField] bool m_Log = false;

        private void OnValidate()
        {
            Log("Unsubsrcibing to Light Bake Events");
            Lightmapping.bakeStarted -= BakeLight;
            Lightmapping.bakeCompleted -= OnBakeCompleted;

            Log("Subscribing to Light Bake Events");
            Lightmapping.bakeStarted += BakeLight;
            Lightmapping.bakeCompleted += OnBakeCompleted;
        }

        private void BakeLight()
        {
            Log("Starting Light Bake");
            ToggleShadowCasting(true);
        }

        private void OnBakeCompleted()
        {
            Log("Light Bake Completed");
            ToggleShadowCasting(false);
        }

        private void ToggleShadowCasting(bool toggle)
        {
            foreach (var renderer in m_StaticRenderers)
            {
                if(renderer == null || m_Filters.Contains(renderer)){ continue; }

                renderer.shadowCastingMode = toggle ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            foreach(Transform t in m_RendererParents)
            {
                foreach (var renderer in t.GetComponentsInChildren<Renderer>())
                {
                    if(renderer == null || m_Filters.Contains(renderer)){ continue; }
                    renderer.shadowCastingMode = toggle ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        private void Log(string message)
        {
            if(m_Log)
                Utils.Log(message);
        }
#endif
    }
}
