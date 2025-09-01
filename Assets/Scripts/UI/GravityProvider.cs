using UnityEngine;

namespace XRMultiplayer
{
    /// <summary>
    /// Simple vertical gravity provider for a <see cref="CharacterController"/>.
    /// Applies gravity when not grounded and can be toggled on/off.
    /// </summary>
    public class GravityProvider : MonoBehaviour
    {
        [SerializeField] private CharacterController m_CharacterController;
        [SerializeField] private float m_Gravity = -9.81f;

        private Vector3 m_VerticalVelocity;

        private void OnValidate()
        {
            if (m_CharacterController == null)
            {
                m_CharacterController = GetComponent<CharacterController>();
            }
        }

        private void Update()
        {
            if (m_CharacterController.isGrounded)
            {
                m_VerticalVelocity.y = 0f;
            }
            else
            {
                m_VerticalVelocity.y += m_Gravity * Time.deltaTime;
                m_CharacterController.Move(m_VerticalVelocity * Time.deltaTime);
            }
        }

        public void SetGravity(bool useGravity)
        {
            enabled = useGravity;
        }
    }
} 