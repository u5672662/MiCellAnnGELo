using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Ensures XRGrabInteractable on a cell is wired to the correct child collider and layer/masks at runtime.
/// Helps when prefab setups drift (wrong layer/mask or offset child), causing rays to not detect the cell.
/// </summary>
public class CellInteractableAutoConfig : MonoBehaviour
{
    [Tooltip("Optional explicit grab collider. If left null, the script will try to find a child named 'GrabInteract' or the first non-trigger collider under this object.")]
    [SerializeField] private Collider grabCollider;

    [Tooltip("If true, will reset the grab collider local transform to identity (0,0,0 / no rotation / scale 1).")]
    [SerializeField] private bool resetGrabColliderTransform = true;

    [Tooltip("Unity layer name that the raycasters (Raycast Mask) and direct interactors (Physics Layer Mask) include.")]
    [SerializeField] private string grabColliderUnityLayer = "Default";

    [Tooltip("Interaction Layer(s) used by the XRGrabInteractable. Must match the Ray Interactors' Interaction Layer Mask.")]
    [SerializeField] private string[] interactionLayerNames = { "Default" };

    private void Awake()
    {
        // Find the interactable on root
        var interactable = GetComponent<XRGrabInteractable>();
        if (interactable == null)
        {
            Debug.LogWarning("[CellInteractableAutoConfig] XRGrabInteractable not found on root.", this);
            return;
        }

        // Resolve grab collider if not assigned
        if (grabCollider == null)
        {
            // Prefer child named 'GrabInteract'
            var t = transform.Find("GrabInteract");
            if (t != null)
                grabCollider = t.GetComponent<Collider>();

            // Fallback: first non-trigger collider in children that is not the root annotation trigger
            if (grabCollider == null)
            {
                grabCollider = GetComponentsInChildren<Collider>(includeInactive: true)
                    .FirstOrDefault(c => c.transform != transform && !c.isTrigger);
            }
        }

        if (grabCollider == null)
        {
            Debug.LogError("[CellInteractableAutoConfig] No suitable grab collider found. Assign one or add a non-trigger child collider.", this);
            return;
        }

        // Normalize collider transform if desired
        if (resetGrabColliderTransform)
        {
            var ct = grabCollider.transform;
            ct.localPosition = Vector3.zero;
            ct.localRotation = Quaternion.identity;
            ct.localScale = Vector3.one;
        }

        // Ensure collider is non-trigger for ray hits
        if (grabCollider.isTrigger)
        {
            grabCollider.isTrigger = false;
        }

        // Ensure collider layer is included by rays/direct interactors
        int unityLayer = LayerMask.NameToLayer(grabColliderUnityLayer);
        if (unityLayer >= 0)
            grabCollider.gameObject.layer = unityLayer;

        // Configure interactable to reference only this collider
        interactable.colliders.Clear();
        interactable.colliders.Add(grabCollider);

        // Set interaction layer mask to match rays
        try
        {
            InteractionLayerMask mask = 0;
            foreach (var name in interactionLayerNames)
            {
                var single = InteractionLayerMask.GetMask(name);
                mask |= single;
            }
            interactable.interactionLayers = mask;
        }
        catch
        {
            // Ignore if names are invalid; leave as-is
        }

        // Safety: if a NetworkBaseInteractable exists, allow override ownership so non-owners can hover/select
        var netBase = GetComponent<XRMultiplayer.NetworkBaseInteractable>();
        if (netBase != null)
        {
            netBase.allowOverrideOwnership = true;
        }
    }
}


