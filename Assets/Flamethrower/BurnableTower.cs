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
    [SerializeField, Min(0f), FormerlySerializedAs("minScaleMultiplier")] private float minPollutionRadius = 1f;
    [SerializeField, Min(0f), FormerlySerializedAs("maxScaleMultiplier")] private float maxPollutionRadius = 4f;
    [SerializeField] private bool makePollutionSphereTrigger = true;
    [SerializeField] private bool disablePollutionWhenIgnited = true;
    [SerializeField] private bool scalePollutionVisual;

    [Header("Ignition")]
    [SerializeField, FormerlySerializedAs("isIgnite")] private bool isIgnited;
    [SerializeField, Min(0)] private int moneyReward = 10;
    [SerializeField] private UnityEvent onIgnited = new UnityEvent();
    [SerializeField] private MoneyRewardEvent onMoneyRewarded = new MoneyRewardEvent();

    [Header("Fire Visual")]
    [SerializeField, FormerlySerializedAs("fireVisualRoot")] private Transform fireVisualRoot;
    [SerializeField, FormerlySerializedAs("fireSystems")] private ParticleSystem[] fireSystems;
    [SerializeField] private bool activateFireObject = true;
    [SerializeField, FormerlySerializedAs("minFireScale")] private float minFireScale = 0.25f;
    [SerializeField, FormerlySerializedAs("maxFireScale")] private float maxFireScale = 2.4f;
    [SerializeField] private bool syncFireEmission = true;
    [SerializeField, FormerlySerializedAs("minEmissionRate")] private float minEmissionRate = 8f;
    [SerializeField, FormerlySerializedAs("maxEmissionRate")] private float maxEmissionRate = 90f;

    [Header("Optional Burned State")]
    [SerializeField, FormerlySerializedAs("disableRenderersWhenBurned")] private bool disableRenderersWhenIgnited;
    [SerializeField, FormerlySerializedAs("renderersToDisable")] private Renderer[] renderersToDisable;
    [SerializeField, FormerlySerializedAs("showDebug")] private bool showDebug;

    private float growthTimer;
    private bool moneyRewardGiven;
    private Vector3 pollutionVisualBaseScale = Vector3.one;
    private Vector3 fireVisualBaseScale = Vector3.one;

    public float Hp => hp;
    public float MaxHp => maxHp;
    public float PollutionRadius => CalculatePollutionRadius();
    public bool IsIgnited => isIgnited;
    public int MoneyReward => moneyReward;

    private void Reset()
    {
        pollutionSphere = GetComponent<SphereCollider>();
        fireSystems = GetComponentsInChildren<ParticleSystem>(true);

        if (fireSystems != null && fireSystems.Length > 0 && fireSystems[0])
        {
            fireVisualRoot = fireSystems[0].transform;
        }
    }

    private void Awake()
    {
        CacheBaseScales();
        ResolveReferences();
        ClampValues();
        ApplyPollutionSphere();
        ApplyIgnitedState(isIgnited, hp);
    }

    private void OnValidate()
    {
        ResolveReferences();
        ClampValues();

        if (!Application.isPlaying)
        {
            CacheBaseScales();
        }

        ApplyPollutionSphere(false);
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
            Ignite(hpBeforeDamage);
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
        Ignite(Mathf.Max(hp, maxHp));
    }

    private void Ignite(float visualHp)
    {
        if (isIgnited)
        {
            return;
        }

        isIgnited = true;
        hp = 0f;
        growthTimer = 0f;

        ApplyPollutionSphere();
        ApplyIgnitedState(true, visualHp);
        GiveMoneyReward();
        onIgnited?.Invoke();
        AnyIgnited?.Invoke(this);

        if (showDebug)
        {
            Debug.Log($"{name} ignited. Reward: {moneyReward}", this);
        }
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

    private void ApplyIgnitedState(bool ignited, float visualHp)
    {
        SetFireActive(ignited, visualHp);
        SetRenderersEnabled(!ignited || !disableRenderersWhenIgnited);
    }

    private void SetFireActive(bool active, float visualHp)
    {
        if (fireVisualRoot)
        {
            if (activateFireObject && fireVisualRoot != transform)
            {
                fireVisualRoot.gameObject.SetActive(active);
            }

            if (active)
            {
                float scale = Mathf.Lerp(minFireScale, maxFireScale, GetHp01(visualHp));
                fireVisualRoot.localScale = fireVisualBaseScale * scale;
            }
        }

        if (fireSystems == null)
        {
            return;
        }

        float emissionRate = Mathf.Lerp(minEmissionRate, maxEmissionRate, GetHp01(visualHp));
        foreach (ParticleSystem fireSystem in fireSystems)
        {
            if (!fireSystem)
            {
                continue;
            }

            if (activateFireObject && fireSystem.transform != transform)
            {
                fireSystem.gameObject.SetActive(active);
            }

            if (syncFireEmission)
            {
                ParticleSystem.EmissionModule emission = fireSystem.emission;
                emission.rateOverTime = active ? emissionRate : 0f;
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
        float radius = isIgnited && disablePollutionWhenIgnited ? 0f : CalculatePollutionRadius();

        if (pollutionSphere)
        {
            pollutionSphere.isTrigger = makePollutionSphereTrigger;
            pollutionSphere.enabled = radius > 0f;
            pollutionSphere.radius = radius;
        }

        if (updateVisual && pollutionVisualRoot && scalePollutionVisual)
        {
            float visualScale = minPollutionRadius > 0f ? radius / minPollutionRadius : radius;
            pollutionVisualRoot.localScale = pollutionVisualBaseScale * Mathf.Max(0f, visualScale);
        }
    }

    private float CalculatePollutionRadius()
    {
        return Mathf.Lerp(minPollutionRadius, maxPollutionRadius, GetHp01(hp));
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
        minPollutionRadius = Mathf.Max(0f, minPollutionRadius);
        maxPollutionRadius = Mathf.Max(minPollutionRadius, maxPollutionRadius);
        minFireScale = Mathf.Max(0f, minFireScale);
        maxFireScale = Mathf.Max(minFireScale, maxFireScale);
        minEmissionRate = Mathf.Max(0f, minEmissionRate);
        maxEmissionRate = Mathf.Max(minEmissionRate, maxEmissionRate);
        moneyReward = Mathf.Max(0, moneyReward);
    }

    private void CacheBaseScales()
    {
        if (pollutionVisualRoot)
        {
            pollutionVisualBaseScale = pollutionVisualRoot.localScale;
        }

        if (fireVisualRoot)
        {
            fireVisualBaseScale = fireVisualRoot.localScale;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isIgnited ? Color.red : new Color(1f, 0.55f, 0f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, CalculatePollutionRadius());
    }
}
