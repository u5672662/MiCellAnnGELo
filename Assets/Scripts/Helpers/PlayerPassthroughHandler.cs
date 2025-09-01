using UnityEngine;

/// <summary>
/// Ensures the local colliders never block CharacterControllers (players) while
/// keeping them as non-triggers so XR grab interaction continues to work.
/// </summary>
public class PlayerPassthroughHandler : MonoBehaviour
{
    private Collider[] _localColliders;
    private float _nextRefreshTime;

    private void Awake()
    {
        _localColliders = GetComponentsInChildren<Collider>(includeInactive: true);
        // Ensure colliders are non-trigger for grabbing
        foreach (var c in _localColliders)
        {
            if (c != null && c.enabled)
                c.isTrigger = false;
        }
        RefreshIgnores();
    }

    private void OnEnable()
    {
        if (_localColliders == null || _localColliders.Length == 0)
            _localColliders = GetComponentsInChildren<Collider>(includeInactive: true);
        RefreshIgnores();
    }

    private void Update()
    {
        // Periodically re-apply ignores to catch late-spawned players
        if (Time.time >= _nextRefreshTime)
        {
            _nextRefreshTime = Time.time + 1.0f;
            RefreshIgnores();
        }
    }

    /// <summary>
    /// Call after meshes/colliders change to re-apply ignore collision with all CharacterControllers.
    /// </summary>
    public void RefreshIgnores()
    {
        if (_localColliders == null || _localColliders.Length == 0)
            _localColliders = GetComponentsInChildren<Collider>(includeInactive: true);

#if UNITY_2023_1_OR_NEWER || UNITY_2022_2_OR_NEWER
        var characterControllers = Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None);
#else
        var characterControllers = Object.FindObjectsOfType<CharacterController>();
#endif
        foreach (var cc in characterControllers)
        {
            if (cc == null) continue;
            foreach (var localCol in _localColliders)
            {
                if (localCol == null || !localCol.enabled) continue;
                Physics.IgnoreCollision(localCol, cc, true);
            }
        }
    }
}
