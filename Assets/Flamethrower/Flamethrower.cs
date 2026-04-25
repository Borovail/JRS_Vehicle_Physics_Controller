using UnityEngine;

public class Flamethrower : MonoBehaviour
{
    [Header("Aim")]
    [SerializeField] private Camera aimCamera;
    [SerializeField] private Transform rotateRoot;
    [SerializeField] private Transform aimOrigin;
    [SerializeField] private LayerMask aimMask = ~0;
    [SerializeField] private QueryTriggerInteraction aimTriggerInteraction = QueryTriggerInteraction.Collide;
    [SerializeField] private float maxAimDistance = 150f;
    [SerializeField] private bool yawOnly;
    [SerializeField] private bool limitPitch;
    [SerializeField] private float minPitch = -35f;
    [SerializeField] private float maxPitch = 45f;
    [SerializeField] private float aimPointSmoothTime = 0.08f;
    [SerializeField] private float maxTurnDegreesPerSecond = 360f;
    [SerializeField] private float minimumAimDistance = 1.5f;
    [SerializeField] private Vector3 rotationOffsetEuler;

    [Header("Fire")]
    [SerializeField] private ParticleSystem fireSystem;
    [SerializeField] private Transform fireOrigin;
    [SerializeField] private float fireDistance = 12f;
    [SerializeField] private float fireRadius = 1.4f;
    [SerializeField] private LayerMask fireMask = ~0;
    [SerializeField] private QueryTriggerInteraction fireTriggerInteraction = QueryTriggerInteraction.Collide;
    [SerializeField] private bool ignoreOwnColliders = true;
    [SerializeField] private bool syncParticleRange = true;
    [SerializeField] private bool debugFireRaycast = true;
    [SerializeField] private int mouseButton = 0;
    [SerializeField] private KeyCode alternateFireKey = KeyCode.None;
    [SerializeField] private bool setParticleObjectsActive;
    [SerializeField] private bool stopEmittingOnly = true;
    [SerializeField] private float burnDamagePerSecond = 8f;
    [SerializeField] private bool alignFireOriginToAimPoint = true;
    [SerializeField] private Vector3 fireRotationOffsetEuler;

    private bool isFiring;
    private bool hasAimPoint;
    private bool hasSmoothedAimPoint;
    private Vector3 aimPoint;
    private Vector3 smoothedAimPoint;
    private Vector3 aimPointVelocity;

    public bool IsFiring => isFiring;

    private void Reset()
    {
        rotateRoot = transform;
        aimOrigin = transform;
        aimCamera = Camera.main;
        fireOrigin = transform;
        fireSystem = GetComponentInChildren<ParticleSystem>(true);
    }

    private void Awake()
    {
        if (!rotateRoot)
        {
            rotateRoot = transform;
        }

        if (!aimOrigin)
        {
            aimOrigin = rotateRoot;
        }

        if (!fireOrigin)
        {
            fireOrigin = aimOrigin;
        }

        if (!aimCamera)
        {
            aimCamera = Camera.main;
        }

        if (!fireSystem)
        {
            fireSystem = GetComponentInChildren<ParticleSystem>(true);
        }

        SyncParticleRange();
        SetFiring(false);
    }

    private void OnValidate()
    {
        maxAimDistance = Mathf.Max(0.1f, maxAimDistance);
        fireDistance = Mathf.Max(0.1f, fireDistance);
        fireRadius = Mathf.Max(0f, fireRadius);
        aimPointSmoothTime = Mathf.Max(0f, aimPointSmoothTime);
        maxTurnDegreesPerSecond = Mathf.Max(1f, maxTurnDegreesPerSecond);
        minimumAimDistance = Mathf.Max(0f, minimumAimDistance);
        burnDamagePerSecond = Mathf.Max(0f, burnDamagePerSecond);

        SyncParticleRange();
    }

    private void Update()
    {
        AimAtMouse();

        bool shouldFire = IsFireInputPressed();
        SetFiring(shouldFire);

        if (shouldFire)
        {
            CastFireRay();
        }
    }

    private void AimAtMouse()
    {
        if (!aimCamera || !rotateRoot || !aimOrigin)
        {
            return;
        }

        Ray ray = aimCamera.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, maxAimDistance, aimMask, aimTriggerInteraction))
        {
            aimPoint = hit.point;
        }
        else
        {
            aimPoint = ray.GetPoint(maxAimDistance);
        }

        hasAimPoint = true;

        if (!hasSmoothedAimPoint)
        {
            smoothedAimPoint = aimPoint;
            hasSmoothedAimPoint = true;
        }
        else
        {
            smoothedAimPoint = Vector3.SmoothDamp(smoothedAimPoint, aimPoint, ref aimPointVelocity, aimPointSmoothTime);
        }

        Vector3 direction = smoothedAimPoint - aimOrigin.position;
        if (yawOnly)
        {
            direction.y = 0f;
        }

        if (direction.sqrMagnitude < minimumAimDistance * minimumAimDistance)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up) * Quaternion.Euler(rotationOffsetEuler);
        if (limitPitch)
        {
            targetRotation = ClampPitch(targetRotation);
        }

        rotateRoot.rotation = Quaternion.RotateTowards(
            rotateRoot.rotation,
            targetRotation,
            maxTurnDegreesPerSecond * Time.deltaTime
        );

        AlignFireOriginToAimPoint();
    }

    private void AlignFireOriginToAimPoint()
    {
        if (!alignFireOriginToAimPoint || !fireOrigin || !hasAimPoint)
        {
            return;
        }

        Vector3 direction = GetFireDirection();
        if (direction.sqrMagnitude <= 0f)
        {
            return;
        }

        fireOrigin.rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(fireRotationOffsetEuler);
    }

    private Quaternion ClampPitch(Quaternion rotation)
    {
        Vector3 euler = rotation.eulerAngles;
        euler.x = NormalizeAngle(euler.x);
        euler.x = Mathf.Clamp(euler.x, minPitch, maxPitch);
        euler.z = 0f;
        return Quaternion.Euler(euler);
    }

    private float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    private bool IsFireInputPressed()
    {
        bool mousePressed = mouseButton >= 0 && Input.GetMouseButton(mouseButton);
        bool keyPressed = alternateFireKey != KeyCode.None && Input.GetKey(alternateFireKey);
        return mousePressed || keyPressed;
    }

    private void CastFireRay()
    {
        if (!fireOrigin)
        {
            return;
        }

        Vector3 fireDirection = GetFireDirection();
        if (CastFire(fireDirection, out RaycastHit hit, out BurnableTower burnableTower))
        {
            if (burnableTower)
            {
                burnableTower.Burn(burnDamagePerSecond * Time.deltaTime);
            }

            if (debugFireRaycast)
            {
                Debug.Log($"Flamethrower hit: {hit.collider.name}", hit.collider);
            }
        }
        else if (debugFireRaycast)
        {
            Debug.Log("Flamethrower hit nothing.");
        }
    }

    private bool CastFire(Vector3 fireDirection, out RaycastHit closestHit, out BurnableTower closestBurnableTower)
    {
        closestHit = default;
        closestBurnableTower = null;

        RaycastHit[] hits = fireRadius > 0f
            ? Physics.SphereCastAll(fireOrigin.position, fireRadius, fireDirection, fireDistance, fireMask, fireTriggerInteraction)
            : Physics.RaycastAll(fireOrigin.position, fireDirection, fireDistance, fireMask, fireTriggerInteraction);

        float closestDistance = float.PositiveInfinity;
        float closestBurnableDistance = float.PositiveInfinity;
        bool hasHit = false;

        foreach (RaycastHit hit in hits)
        {
            if (!hit.collider || IsOwnCollider(hit.collider))
            {
                continue;
            }

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestHit = hit;
                hasHit = true;
            }

            BurnableTower burnableTower = GetBurnableTower(hit.collider);
            if (burnableTower && hit.distance < closestBurnableDistance)
            {
                closestBurnableDistance = hit.distance;
                closestBurnableTower = burnableTower;
                closestHit = hit;
                hasHit = true;
            }
        }

        return hasHit;
    }

    private bool IsOwnCollider(Collider hitCollider)
    {
        return ignoreOwnColliders && hitCollider.transform.IsChildOf(transform);
    }

    private BurnableTower GetBurnableTower(Collider hitCollider)
    {
        BurnableTower burnableTower = hitCollider.GetComponentInParent<BurnableTower>();
        if (!burnableTower)
        {
            burnableTower = hitCollider.GetComponentInChildren<BurnableTower>();
        }

        return burnableTower;
    }

    private Vector3 GetFireDirection()
    {
        if (fireOrigin && hasAimPoint)
        {
            Vector3 direction = aimPoint - fireOrigin.position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                return direction.normalized;
            }
        }

        return fireOrigin ? fireOrigin.forward : transform.forward;
    }

    private void SyncParticleRange()
    {
        if (!syncParticleRange || !fireSystem)
        {
            return;
        }

        ParticleSystem.MainModule main = fireSystem.main;

        main.startSpeed = fireDistance * 1.5f;
    }

    private void SetFiring(bool shouldFire)
    {
        if (isFiring == shouldFire)
        {
            return;
        }

        isFiring = shouldFire;

        if (!fireSystem)
        {
            return;
        }

        if (setParticleObjectsActive)
        {
            fireSystem.gameObject.SetActive(shouldFire);
        }

        if (shouldFire)
        {
            SyncParticleRange();
            fireSystem.Play(true);
        }
        else if (stopEmittingOnly)
        {
            fireSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        else
        {
            fireSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!fireOrigin)
        {
            return;
        }

        Gizmos.color = Color.red;
        Vector3 fireDirection = GetFireDirection();
        Gizmos.DrawLine(fireOrigin.position, fireOrigin.position + fireDirection * fireDistance);

        if (fireRadius > 0f)
        {
            Gizmos.DrawWireSphere(fireOrigin.position + fireDirection * fireDistance, fireRadius);
        }
    }
}
