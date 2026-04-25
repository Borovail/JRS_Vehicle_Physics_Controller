using UnityEngine;

public class MouseAimedFlamethrower : MonoBehaviour
{
    [Header("Aim")]
    [SerializeField] private Camera aimCamera;
    [SerializeField] private Transform rotateRoot;
    [SerializeField] private Transform aimOrigin;
    [SerializeField] private LayerMask aimMask = ~0;
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
    [SerializeField] private ParticleSystem[] fireSystems;
    [SerializeField] private int mouseButton = 0;
    [SerializeField] private KeyCode alternateFireKey = KeyCode.None;
    [SerializeField] private bool setParticleObjectsActive;
    [SerializeField] private bool stopEmittingOnly = true;

    private bool isFiring;
    private bool hasSmoothedAimPoint;
    private Vector3 smoothedAimPoint;
    private Vector3 aimPointVelocity;

    public bool IsFiring => isFiring;

    private void Reset()
    {
        rotateRoot = transform;
        aimOrigin = transform;
        aimCamera = Camera.main;
        fireSystems = GetComponentsInChildren<ParticleSystem>(true);
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

        if (!aimCamera)
        {
            aimCamera = Camera.main;
        }

        if (fireSystems == null || fireSystems.Length == 0)
        {
            fireSystems = GetComponentsInChildren<ParticleSystem>(true);
        }

        SetFiring(false);
    }

    private void Update()
    {
        AimAtMouse();
        SetFiring(IsFireInputPressed());
    }

    private void AimAtMouse()
    {
        if (!aimCamera || !rotateRoot || !aimOrigin)
        {
            return;
        }

        Ray ray = aimCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 aimPoint;

        if (Physics.Raycast(ray, out RaycastHit hit, maxAimDistance, aimMask, QueryTriggerInteraction.Ignore))
        {
            aimPoint = hit.point;
        }
        else
        {
            Plane aimPlane = new Plane(Vector3.up, aimOrigin.position);
            aimPoint = aimPlane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : ray.GetPoint(maxAimDistance);
        }

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

    private void SetFiring(bool shouldFire)
    {
        if (isFiring == shouldFire)
        {
            return;
        }

        isFiring = shouldFire;

        if (fireSystems == null)
        {
            return;
        }

        foreach (ParticleSystem fireSystem in fireSystems)
        {
            if (!fireSystem)
            {
                continue;
            }

            if (setParticleObjectsActive)
            {
                fireSystem.gameObject.SetActive(shouldFire);
            }

            if (shouldFire)
            {
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
    }
}
