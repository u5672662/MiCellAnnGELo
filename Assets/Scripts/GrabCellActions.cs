using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Handles grabbing and two‑hand scaling interactions for the cell object, and syncs state over the network.
/// Supports direct InputAction and InputActionReference bindings.
/// </summary>
public class GrabCellActions : NetworkBehaviour
{
    [Header("Input Actions")]
    public InputAction grab;
    public InputActionReference grabActionReference;
    
    [Header("References")]
    public GameObject otherController;
    public Transform cell, pointerSphere;
    
    [Header("Debug")]
    public bool isGrabbing = false;

    private NetworkVariable<bool> _networkIsGrabbing = new NetworkVariable<bool>(false);

    private bool _isScaling = false;
    private float _originalDistance;
    private Vector3 _cellOriginalScale;
    private Vector3 _pointerOriginalScale;
    private Vector3 _cellOriginalLocalPos;
    private UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor _interactor;

    private void Awake()
    {
        // Try to get the XR interactor if available
        _interactor = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>();
    }
    
    /// <inheritdoc />
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _networkIsGrabbing.OnValueChanged += (prev, next) => { isGrabbing = next; };
        isGrabbing = _networkIsGrabbing.Value;
    }
    
    private void OnEnable()
    {
        // Enable the grab action
        if (grab != null)
        {
            grab.Enable();
        }
        
        // Subscribe to action events if using action reference
        if (grabActionReference != null && grabActionReference.action != null)
        {
            grabActionReference.action.performed += OnGrabPerformed;
            grabActionReference.action.canceled += OnGrabCanceled;
            grabActionReference.action.Enable();
        }
    }
    
    private void OnDisable()
    {
        // Disable the grab action
        if (grab != null)
        {
            grab.Disable();
        }
        
        // Unsubscribe from action events
        if (grabActionReference != null && grabActionReference.action != null)
        {
            grabActionReference.action.performed -= OnGrabPerformed;
            grabActionReference.action.canceled -= OnGrabCanceled;
            grabActionReference.action.Disable();
        }
    }
    
    // Update is called once per frame
    private void Update()
    {
        // Support both direct InputAction and legacy input system
        if (grab != null && grab.WasPressedThisFrame())
            Grab();

        if (grab != null && grab.WasReleasedThisFrame())
            Release();

        if (_isScaling)
            Scale();
    }
    
    // Event handlers for InputActionReference
    private void OnGrabPerformed(InputAction.CallbackContext context)
    {
        Grab();
    }
    
    private void OnGrabCanceled(InputAction.CallbackContext context)
    {
        Release();
    }

    /// <summary>
    /// Attempts to grab the cell; if already holding, begins two‑hand scaling.
    /// </summary>
    private void Grab()
    {
        isGrabbing = true;
        _networkIsGrabbing.Value = true;
        GrabServerRpc(NetworkManager.Singleton.LocalClientId);
        
        // Check if the cell isn't currently being held
        if (cell != null && cell.parent == null)
        {
            // Attach cell to hand
            cell.SetParent(transform, true);
            
            // Add haptic feedback if available
            if (_interactor != null)
            {
                var hapticDevice = _interactor.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInputInteractor>();
                if (hapticDevice != null)
                {
                    hapticDevice.SendHapticImpulse(0.5f, 0.1f);
                }
            }
        }
        else
        {
            // Cell attached to hand so start scaling
            _isScaling = true;
            // Store parameters before scaling 
            if (cell != null)
                _cellOriginalScale = cell.localScale;
            if (pointerSphere != null)
                _pointerOriginalScale = pointerSphere.localScale;
            if (otherController != null)
                _originalDistance = Vector3.Distance(transform.position, otherController.transform.position);
            if (cell != null)
                _cellOriginalLocalPos = cell.localPosition;
        }
    }

    /// <summary>
    /// Releases the cell or stops scaling depending on current state; notifies other clients.
    /// </summary>
    private void Release()
    {
        isGrabbing = false;
        _networkIsGrabbing.Value = false;
        ReleaseServerRpc(NetworkManager.Singleton.LocalClientId);
        
        // Check if this hand is attached to the cell
        if (cell != null && cell.parent == transform)
        {
            // Set cell parent to world space
            cell.SetParent(null, true);
            // Stop other controller from scaling
            var otherGrabActions = otherController != null ? otherController.GetComponent<GrabCellActions>() : null;
            if (otherGrabActions != null)
            {
                otherGrabActions.StopScaling();
            }
            
            // Add haptic feedback if available
            if (_interactor != null)
            {
                var hapticDevice = _interactor.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInputInteractor>();
                if (hapticDevice != null)
                {
                    hapticDevice.SendHapticImpulse(0.3f, 0.1f);
                }
            }
        }
        else
            StopScaling();
    }

    private void Scale()
    {
        if (cell == null || pointerSphere == null || otherController == null || _originalDistance == 0f)
            return;
        // Calculate distance between controllers
        float newDist = Vector3.Distance(transform.position, otherController.transform.position);
        // Calculate multiple of original distance
        float scaleChange = newDist / _originalDistance;
        // Scale all objects
        pointerSphere.localScale = _pointerOriginalScale * scaleChange;
        cell.localScale = _cellOriginalScale * scaleChange;
        cell.localPosition = _cellOriginalLocalPos * scaleChange;
    }

    public void StopScaling()
    {
        _isScaling = false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void GrabServerRpc(ulong clientId)
    {
        GrabClientRpc(clientId);
    }

    [ClientRpc]
    private void GrabClientRpc(ulong clientId)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId)
        {
            ExecuteGrabRemote();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReleaseServerRpc(ulong clientId)
    {
        ReleaseClientRpc(clientId);
    }

    [ClientRpc]
    private void ReleaseClientRpc(ulong clientId)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId)
        {
            ExecuteReleaseRemote();
        }
    }

    private void ExecuteGrabRemote()
    {
        isGrabbing = true;
    }

    private void ExecuteReleaseRemote()
    {
        isGrabbing = false;
    }
}