using UnityEngine;
using UnityEngine.UI;

namespace XRMultiplayer
{
    [RequireComponent(typeof(Toggle))]
    /// <summary>
    /// Simple toggle-driven tooltip controller. Ensures required references and hides/shows the tooltip object.
    /// </summary>
    public class TooltipUI : MonoBehaviour
    {
        [SerializeField] GameObject m_TooltipObject;
        [SerializeField] bool m_StartShowing = false;
        Toggle m_Toggle;

        private void Awake()
        {
            if (!TryGetComponent(out m_Toggle) || m_TooltipObject == null)
            {
                Utils.Log($"{gameObject.name} Missing Setup Requirements! Disabling Now.", 2);
                gameObject.SetActive(false);
                enabled = false;
                return;
            }
            m_Toggle.onValueChanged.AddListener(OnToggle);
            ResetTooltip();
        }

        private void OnDestroy()
        {
            m_Toggle.onValueChanged.RemoveListener(OnToggle);
        }

        private void OnToggle(bool toggle)
        {
            if (toggle)
            {
                ShowTooltip();
            }
            else
            {
                HideTooltip();
            }
        }

        public void ShowTooltip()
        {
            m_TooltipObject.SetActive(true);
        }

        public void HideTooltip()
        {
            if (m_Toggle.isOn) return;
            m_TooltipObject.SetActive(false);
        }

        public void ResetTooltip()
        {
            m_Toggle.SetIsOnWithoutNotify(false);
            HideTooltip();
            if (m_StartShowing)
            {
                m_Toggle.isOn = true;
            }
        }
    }
}
