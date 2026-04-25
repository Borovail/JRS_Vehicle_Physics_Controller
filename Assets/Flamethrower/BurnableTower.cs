using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class BurnableTower : MonoBehaviour
{
    [Serializable]
    public class MoneyRewardEvent : UnityEvent<int>
    {
    }

    public static event Action<BurnableTower> AnyIgnited;
    public static event Action<BurnableTower, int> AnyMoneyRewarded;

    [Header("Health")]
    [SerializeField, Min(0f), FormerlySerializedAs("startHealth")] private float hp = 1f;
    [SerializeField, Min(0.01f), FormerlySerializedAs("maxHealth")] private float maxHp = 20f;

    [Header("Growth")]
    [SerializeField, Min(0f), FormerlySerializedAs("healthGrowthPerSecond")] private float hpGrowthValue = 1f;
    [SerializeField, Min(0.01f)] private float hpGrowthTime = 1f;

    [Header("Pollution Sphere")]
    [SerializeField] private SphereCollider pollutionSphere;
    [SerializeField, FormerlySerializedAs("scalableRoot")] private Transform pollutionVisualRoot;
    [SerializeField, Min(1f), FormerlySerializedAs("maxScaleMultiplier"), FormerlySerializedAs("maxPollutionRadius")] private float maxPollutionScaleMultiplier = 4f;
    [SerializeField] private bool makePollutionSphereTrigger = true;
    [SerializeField] private bool disablePollutionWhenIgnited = true;

    [Header("Ignition")]
    [SerializeField, FormerlySerializedAs("isIgnite")] private bool isIgnited;
    [SerializeField, Min(0)] private int moneyReward = 10;
    [SerializeField] private UnityEvent onIgnited = new UnityEvent();
    [SerializeField] private MoneyRewardEvent onMoneyRewarded = new MoneyRewardEvent();

    [Header("Fire Visual")]
    [SerializeField, FormerlySerializedAs("fireVisualRoot")] private Transform fireVisualRoot;
    [SerializeField, FormerlySerializedAs("fireSystems")] private ParticleSystem[] fireSystems;

    [Header("Destroy")]
    [SerializeField] private bool destroyTowerWhenIgnited = true;
    [SerializeField, Min(0f)] private float destroyDelay;
    [SerializeField] private bool detachFireBeforeDestroy = true;

    [Header("Optional Burned State")]
    [SerializeField, FormerlySerializedAs("disableRenderersWhenBurned")] private bool disableRenderersWhenIgnited;
    [SerializeField, FormerlySerializedAs("renderersToDisable")] private Renderer[] renderersToDisable;
    [SerializeField, FormerlySerializedAs("showDebug")] private bool showDebug;

    private float growthTimer;
    private bool moneyRewardGiven;
    private Vector3 pollutionVisualBaseScale = Vector3.one;

    public float Hp => hp;
    public float MaxHp => maxHp;
    public float PollutionScaleMultiplier => CalculatePollutionScaleMultiplier();
    public bool IsIgnited => isIgnited;
    public int MoneyReward => moneyReward;

    private void Reset()
    {
        pollutionSphere = GetComponent<SphereCollider>();
        pollutionVisualRoot = pollutionSphere ? pollutionSphere.transform : transform;
        fireSystems = GetComponentsInChildren<ParticleSystem>(true);

        if (fireSystems != null && fireSystems.Length > 0 && fireSystems[0])
        {
            fireVisualRoot = fireSystems[0].transform;
        }
    }

    private void Awake()
    {
        ResolveReferences();
        CacheBaseScales();
        ClampValues();
        ApplyPollutionSphere();
        ApplyIgnitedState(isIgnited);
    }

    private void OnValidate()
    {
        ResolveReferences();
        ClampValues();

        if (!Application.isPlaying)
        {
            CacheBaseScales();
        }
    }

    private void Update()
    {
        if (isIgnited || hp >= maxHp || hpGrowthValue <= 0f)
        {
            return;
        }

        growthTimer += Time.deltaTime;
        if (growthTimer < hpGrowthTime)
        {
            return;
        }

        int growthSteps = Mathf.FloorToInt(growthTimer / hpGrowthTime);
        growthTimer -= growthSteps * hpGrowthTime;
        AddHp(hpGrowthValue * growthSteps);
    }

    public void Burn(float damage)
    {
        if (isIgnited || damage <= 0f)
        {
            return;
        }

        float hpBeforeDamage = hp;
        hp = Mathf.Max(0f, hp - damage);
        growthTimer = 0f;
        ApplyPollutionSphere();

        if (showDebug)
        {
            Debug.Log($"{name} burned: {hpBeforeDamage:0.##} -> {hp:0.##}", this);
        }

        if (hp <= 0f)
        {
            Ignite();
        }
    }

    public void AddHp(float amount)
    {
        if (isIgnited || amount <= 0f)
        {
            return;
        }

        hp = Mathf.Min(maxHp, hp + amount);
        ApplyPollutionSphere();
    }

    public void Ignite()
    {
        if (isIgnited)
        {
            return;
        }

        isIgnited = true;
        hp = 0f;
        growthTimer = 0f;

        ApplyPollutionSphere();
        ApplyIgnitedState(true);
        GiveMoneyReward();
        onIgnited?.Invoke();
        AnyIgnited?.Invoke(this);

        if (showDebug)
        {
            Debug.Log($"{name} ignited. Reward: {moneyReward}", this);
        }

        DestroyTowerIfNeeded();
    }

    private void GiveMoneyReward()
    {
        if (moneyRewardGiven)
        {
            return;
        }

        moneyRewardGiven = true;
        onMoneyRewarded?.Invoke(moneyReward);
        AnyMoneyRewarded?.Invoke(this, moneyReward);
    }

    private void ApplyIgnitedState(bool ignited)
    {
        SetFireActive(ignited);
        SetRenderersEnabled(!ignited || !disableRenderersWhenIgnited);
    }

    private void SetFireActive(bool active)
    {
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

            if (active)
            {
                fireSystem.Play(true);
            }
            else
            {
                fireSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }

    private void DestroyTowerIfNeeded()
    {
        if (!destroyTowerWhenIgnited)
        {
            return;
        }

        if (detachFireBeforeDestroy)
        {
            DetachFireVisuals();
        }

        Destroy(gameObject, destroyDelay);
    }

    private void DetachFireVisuals()
    {
        if (fireVisualRoot && fireVisualRoot != transform && fireVisualRoot.IsChildOf(transform))
        {
            fireVisualRoot.SetParent(null, true);
            return;
        }

        if (fireSystems == null)
        {
            return;
        }

        foreach (ParticleSystem fireSystem in fireSystems)
        {
            if (fireSystem && fireSystem.transform != transform && fireSystem.transform.IsChildOf(transform))
            {
                fireSystem.transform.SetParent(null, true);
            }
        }
    }

    private void SetRenderersEnabled(bool enabled)
    {
        if (renderersToDisable == null)
        {
            return;
        }

        foreach (Renderer rendererToDisable in renderersToDisable)
        {
            if (rendererToDisable)
            {
                rendererToDisable.enabled = enabled;
            }
        }
    }

    private void ApplyPollutionSphere(bool updateVisual = true)
    {
        if (pollutionSphere)
        {
            pollutionSphere.isTrigger = makePollutionSphereTrigger;
            pollutionSphere.enabled = !isIgnited || !disablePollutionWhenIgnited;
        }

        if (updateVisual && pollutionVisualRoot)
        {
            float scale = CalculatePollutionScaleMultiplier();
            pollutionVisualRoot.localScale = pollutionVisualBaseScale * Mathf.Max(0f, scale);
        }
    }

    private float CalculatePollutionScaleMultiplier()
    {
        return Mathf.Lerp(1f, maxPollutionScaleMultiplier, GetHp01(hp));
    }

    private float GetHp01(float health)
    {
        return maxHp > 0f ? Mathf.Clamp01(health / maxHp) : 0f;
    }

    private void ResolveReferences()
    {
        if (!pollutionSphere)
        {
            pollutionSphere = GetComponent<SphereCollider>();
        }

        if (!pollutionVisualRoot && pollutionSphere)
        {
            pollutionVisualRoot = pollutionSphere.transform;
        }

        if ((fireSystems == null || fireSystems.Length == 0) && fireVisualRoot)
        {
            fireSystems = fireVisualRoot.GetComponentsInChildren<ParticleSystem>(true);
        }
        else if ((fireSystems == null || fireSystems.Length == 0) && !fireVisualRoot)
        {
            fireSystems = GetComponentsInChildren<ParticleSystem>(true);
        }

        if (!fireVisualRoot && fireSystems != null && fireSystems.Length > 0 && fireSystems[0])
        {
            fireVisualRoot = fireSystems[0].transform;
        }
    }

    private void ClampValues()
    {
        maxHp = Mathf.Max(0.01f, maxHp);
        hp = Mathf.Clamp(hp, 0f, maxHp);
        hpGrowthValue = Mathf.Max(0f, hpGrowthValue);
        hpGrowthTime = Mathf.Max(0.01f, hpGrowthTime);
        maxPollutionScaleMultiplier = Mathf.Max(1f, maxPollutionScaleMultiplier);
        destroyDelay = Mathf.Max(0f, destroyDelay);
        moneyReward = Mathf.Max(0, moneyReward);
    }

    private void CacheBaseScales()
    {
        if (pollutionVisualRoot)
        {
            pollutionVisualBaseScale = pollutionVisualRoot.localScale;
        }

    }

    private void OnDrawGizmosSelected()
    {
        if (!pollutionSphere)
        {
            return;
        }

        Gizmos.color = isIgnited ? Color.red : new Color(1f, 0.55f, 0f, 0.35f);
        Gizmos.DrawWireSphere(pollutionSphere.transform.TransformPoint(pollutionSphere.center), pollutionSphere.radius * GetLargestAxis(pollutionSphere.transform.lossyScale));
    }

    private float GetLargestAxis(Vector3 scale)
    {
        return Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
    }
}
