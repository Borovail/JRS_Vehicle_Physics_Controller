// MIT License
//
// Copyright (c) 2023 Samborlang Pyrtuh
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Collections;
using UnityEngine;

public class JrsVehicleController : MonoBehaviour
{
    public float motorForce = 50f;
    public float maxSteerAngle = 30f;
    public WheelCollider frontLeftWheel, frontRightWheel, rearLeftWheel, rearRightWheel;
    public Transform frontLeftWheelTransform, frontRightWheelTransform, rearLeftWheelTransform, rearRightWheelTransform;

    public TrailRenderer[] tireMarks;
    public GameObject centerOfMassObject;
    private Rigidbody rb;
    public WheelCollider[] wheelCollidersBrake;
    public float brakeForce = 500f;

    [Header("Drift")]
    public bool enableDrift = true;
    public float minimumDriftSpeedKmph = 12f;
    public float handbrakeFrontBrakeRatio = 0f;
    public float driftRearSidewaysStiffness = 0.55f;
    public float driftRearForwardStiffness = 0.85f;
    public float driftGripChangeSpeed = 6f;
    public float driftYawAssist = 2.5f;
    public float driftHoldSlipThreshold = 0.22f;

    [Header("Stability Assist")]
    public bool enableStabilityAssist = true;
    public float highSpeedSteerAssistStartKmph = 45f;
    public float highSpeedSteerAssistFullKmph = 120f;
    public float highSpeedMaxSteerInput = 0.45f;
    public float stabilitySlipThreshold = 0.28f;
    public float stabilityYawDamping = 2.25f;
    public float stabilityLateralDamping = 0.7f;

    [Header("Transmission")]
    public float[] gearRatios = { 3.1f, 2.05f, 1.45f, 1.1f, 0.85f };
    public float finalDriveRatio = 3.42f;
    public float idleRPM = 850f;
    public float maxEngineRPM = 6500f;
    public float shiftThreshold = 5600f;
    public float downshiftThreshold = 2200f;
    public float shiftHysteresisRPM = 600f;
    public float gearShiftDelay = 0.45f;
    public float clutchResponse = 8f;
    public float drivetrainTorqueScale = 0.55f;
    private int currentGear = 1; // Variable to track the current gear

    public bool enable4x4 = false; // Option to enable 4-wheel drive

    private float stopSpeedThreshold = 1f; // Speed threshold for considering the vehicle stopped

    public ParticleSystem frontLeftDustParticleSystem, frontRightDustParticleSystem, rearLeftDustParticleSystem, rearRightDustParticleSystem; // References to the dust particle systems for each wheel

    private JrsInputController mobileInputController;

    public AudioSource engineAudioSource; // Assign this in the Inspector
    public AudioSource engineStartAudioSource; // Assign this in the Inspector

    private bool hasStartedMoving = false;
    private WheelCollider[] cachedWheels;
    private WheelFrictionCurve[] defaultForwardFrictions;
    private WheelFrictionCurve[] defaultSidewaysFrictions;
    private float driftBlend;
    private float lastSpeedKmph;
    private float lastWheelSpeedKmph;
    private float lastEngineRPM;
    private float lastTargetEngineRPM;
    private float lastDrivenWheelRPM;
    private float lastThrottleInput;
    private float lastSteerInput;
    private float lastEffectiveSteerInput;
    private float lastAdjustedTorque;
    private float lastRearSidewaysSlip;
    private float gearShiftTimer;
    private bool isHandbrakeActive;
    private bool isDrifting;
    private bool isStabilityAssisting;

#if UNITY_EDITOR
    [Header("Editor Debug")]
    public bool showDebugGui = true;
    public Vector2 debugGuiPosition = new Vector2(10f, 10f);
#endif

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        mobileInputController = FindFirstObjectByType<JrsInputController>();

        cachedWheels = new WheelCollider[] { frontLeftWheel, frontRightWheel, rearLeftWheel, rearRightWheel };
        CacheDefaultFriction();

        StartCoroutine(DelayedEngineSound());
    }

    IEnumerator DelayedEngineSound()
    {
        while (!hasStartedMoving)
        {
            yield return null;
        }

        yield return new WaitForSeconds(2f); // Delay for 2 seconds

        if (engineAudioSource)
        {
            engineAudioSource.Play();
        }
    }


    void Update()
    {
        if (centerOfMassObject)
        {
            rb.centerOfMass = transform.InverseTransformPoint(centerOfMassObject.transform.position);
        }

        UpdateWheelPoses();
    }

    void FixedUpdate()
    {
        lastThrottleInput = GetThrottleInput();
        lastSteerInput = GetSteerInput();
        isHandbrakeActive = IsHandbrakePressed();

        float v = lastThrottleInput * motorForce;

        // Calculate the current wheel speed in km/h
        lastWheelSpeedKmph = frontLeftWheel.radius * Mathf.PI * frontLeftWheel.rpm * 60f / 1000f;
        lastSpeedKmph = rb.linearVelocity.magnitude * 3.6f;
        lastEffectiveSteerInput = GetStabilizedSteerInput(lastSteerInput);
        float h = lastEffectiveSteerInput * maxSteerAngle;

        lastDrivenWheelRPM = GetAverageDrivenWheelRPM();
        UpdateEngineRPM();
        UpdateGear();

        // Adjust the motor torque based on the current gear ratio
        float adjustedTorque = v * GetCurrentGearRatio() * Mathf.Max(0.01f, finalDriveRatio) * drivetrainTorqueScale;
        lastAdjustedTorque = adjustedTorque;

        // Apply motor torque to the wheels
        if (enable4x4)
        {
            frontLeftWheel.motorTorque = adjustedTorque;
            frontRightWheel.motorTorque = adjustedTorque;
            rearLeftWheel.motorTorque = isHandbrakeActive ? 0f : adjustedTorque;
            rearRightWheel.motorTorque = isHandbrakeActive ? 0f : adjustedTorque;
        }
        else
        {
            frontLeftWheel.motorTorque = adjustedTorque;
            frontRightWheel.motorTorque = adjustedTorque;
            rearLeftWheel.motorTorque = 0f; // No torque applied to rear wheels
            rearRightWheel.motorTorque = 0f; // No torque applied to rear wheels
        }

        frontLeftWheel.steerAngle = h;
        frontRightWheel.steerAngle = h;

        ApplyHandbrake();
        lastRearSidewaysSlip = GetAverageRearSidewaysSlip();
        UpdateDriftPhysics();
        UpdateStabilityAssist();
        UpdateWheelPoses();

        // Check if the vehicle is in motion
        bool isMoving = rb.linearVelocity.magnitude > 0.1f;

        // Check if any of the wheels are slipping or drifting
        bool isFrontLeftSlipping = IsWheelSlipping(frontLeftWheel);
        bool isFrontRightSlipping = IsWheelSlipping(frontRightWheel);
        bool isRearLeftSlipping = IsWheelSlipping(rearLeftWheel);
        bool isRearRightSlipping = IsWheelSlipping(rearRightWheel);

        bool isFrontLeftDrifting = IsWheelDrifting(frontLeftWheel);
        bool isFrontRightDrifting = IsWheelDrifting(frontRightWheel);
        bool isRearLeftDrifting = IsWheelDrifting(rearLeftWheel);
        bool isRearRightDrifting = IsWheelDrifting(rearRightWheel);

        isDrifting = driftBlend > 0.05f;

        // Check if any of the wheels are sliding while the brake is applied and the vehicle is in motion
        bool isFrontLeftBraking = IsWheelBraking(frontLeftWheel) && isMoving;
        bool isFrontRightBraking = IsWheelBraking(frontRightWheel) && isMoving;
        bool isRearLeftBraking = IsWheelBraking(rearLeftWheel) && isMoving;
        bool isRearRightBraking = IsWheelBraking(rearRightWheel) && isMoving;

        // Enable/disable the dust particle systems based on wheel slip, drifting, braking, and vehicle motion
        bool shouldPlayDustParticles = (isFrontLeftSlipping || isFrontLeftDrifting || isFrontLeftBraking) ||
                                       (isFrontRightSlipping || isFrontRightDrifting || isFrontRightBraking) ||
                                       (isRearLeftSlipping || isRearLeftDrifting || isRearLeftBraking) ||
                                       (isRearRightSlipping || isRearRightDrifting || isRearRightBraking);

        SetDustParticleSystemState(frontLeftDustParticleSystem, shouldPlayDustParticles);
        SetDustParticleSystemState(frontRightDustParticleSystem, shouldPlayDustParticles);
        SetDustParticleSystemState(rearLeftDustParticleSystem, shouldPlayDustParticles);
        SetDustParticleSystemState(rearRightDustParticleSystem, shouldPlayDustParticles);

        // Calculate the target pitch based on the current speed and direction
        float targetPitch = Mathf.Lerp(0.6f, 2f, Mathf.InverseLerp(idleRPM, maxEngineRPM, lastEngineRPM));

        // Smoothly adjust the pitch towards the target pitch
        if (engineAudioSource)
        {
            engineAudioSource.pitch = Mathf.Lerp(engineAudioSource.pitch, targetPitch, Time.deltaTime * 5f);
        }


        // Play the engine start sound if the vehicle just starts moving
        if (!hasStartedMoving && lastSpeedKmph > 0.1f)
        {
            if (engineStartAudioSource)
            {
                engineStartAudioSource.Play();
            }

            hasStartedMoving = true;
        }
    }

    float GetThrottleInput()
    {
        return mobileInputController != null ? mobileInputController.GetVerticalInput() : Input.GetAxis("Vertical");
    }

    float GetSteerInput()
    {
        return mobileInputController != null ? mobileInputController.GetHorizontalInput() : Input.GetAxis("Horizontal");
    }

    bool IsHandbrakePressed()
    {
        bool mobileBrakePressed = mobileInputController != null &&
                                  mobileInputController.brakeButton != null &&
                                  mobileInputController.brakeButton.IsButtonPressed();

        return Input.GetKey(KeyCode.Space) || mobileBrakePressed;
    }

    float GetCurrentGearRatio()
    {
        if (gearRatios == null || gearRatios.Length == 0)
        {
            return 1f;
        }

        return gearRatios[Mathf.Clamp(currentGear - 1, 0, gearRatios.Length - 1)];
    }

    void UpdateEngineRPM()
    {
        float connectedRPM = GetEstimatedRPMForGear(currentGear);
        float launchRPM = Mathf.Lerp(idleRPM, idleRPM + 1800f, Mathf.Abs(lastThrottleInput));

        if (lastSpeedKmph < 8f)
        {
            connectedRPM = Mathf.Max(connectedRPM, launchRPM);
        }

        lastTargetEngineRPM = Mathf.Clamp(connectedRPM, idleRPM, maxEngineRPM);
        lastEngineRPM = Mathf.Lerp(lastEngineRPM <= 0f ? idleRPM : lastEngineRPM, lastTargetEngineRPM, Mathf.Clamp01(clutchResponse * Time.fixedDeltaTime));
    }

    float GetEstimatedRPMForGear(int gear)
    {
        if (gearRatios == null || gearRatios.Length == 0)
        {
            return idleRPM;
        }

        int gearIndex = Mathf.Clamp(gear - 1, 0, gearRatios.Length - 1);
        return Mathf.Abs(lastDrivenWheelRPM) * gearRatios[gearIndex] * Mathf.Max(0.01f, finalDriveRatio);
    }

    float GetAverageDrivenWheelRPM()
    {
        float rpm = 0f;
        int count = 0;

        AddDrivenWheelRPM(frontLeftWheel, ref rpm, ref count);
        AddDrivenWheelRPM(frontRightWheel, ref rpm, ref count);

        if (enable4x4 && !isHandbrakeActive)
        {
            AddDrivenWheelRPM(rearLeftWheel, ref rpm, ref count);
            AddDrivenWheelRPM(rearRightWheel, ref rpm, ref count);
        }

        return count > 0 ? rpm / count : 0f;
    }

    void AddDrivenWheelRPM(WheelCollider wheel, ref float rpm, ref int count)
    {
        if (!wheel)
        {
            return;
        }

        rpm += wheel.rpm;
        count++;
    }

    void UpdateGear()
    {
        int gearCount = gearRatios != null ? gearRatios.Length : 0;
        if (gearCount == 0)
        {
            currentGear = 1;
            return;
        }

        currentGear = Mathf.Clamp(currentGear, 1, gearCount);
        gearShiftTimer = Mathf.Max(0f, gearShiftTimer - Time.fixedDeltaTime);

        if (gearShiftTimer > 0f)
        {
            return;
        }

        bool shouldForceUpshift = lastEngineRPM > maxEngineRPM;

        if ((lastEngineRPM > shiftThreshold || shouldForceUpshift) && currentGear < gearCount)
        {
            float nextGearRPM = GetEstimatedRPMForGear(currentGear + 1);
            if (!shouldForceUpshift && nextGearRPM < downshiftThreshold + shiftHysteresisRPM)
            {
                return;
            }

            currentGear++;
            gearShiftTimer = gearShiftDelay;
        }
        else if ((lastEngineRPM < downshiftThreshold || lastSpeedKmph < stopSpeedThreshold) && currentGear > 1)
        {
            float previousGearRPM = GetEstimatedRPMForGear(currentGear - 1);
            if (previousGearRPM > shiftThreshold - shiftHysteresisRPM && lastSpeedKmph >= stopSpeedThreshold)
            {
                return;
            }

            currentGear--;
            gearShiftTimer = gearShiftDelay;
        }
    }

    float GetStabilizedSteerInput(float rawSteerInput)
    {
        if (!enableStabilityAssist || isHandbrakeActive)
        {
            return rawSteerInput;
        }

        float speedFactor = Mathf.InverseLerp(highSpeedSteerAssistStartKmph, highSpeedSteerAssistFullKmph, lastSpeedKmph);
        float maxInput = Mathf.Lerp(1f, Mathf.Clamp01(highSpeedMaxSteerInput), speedFactor);
        return Mathf.Clamp(rawSteerInput, -maxInput, maxInput);
    }

    void ApplyHandbrake()
    {
        if (isHandbrakeActive)
        {
            SetBrakeTorque(frontLeftWheel, brakeForce * handbrakeFrontBrakeRatio);
            SetBrakeTorque(frontRightWheel, brakeForce * handbrakeFrontBrakeRatio);
            SetBrakeTorque(rearLeftWheel, brakeForce);
            SetBrakeTorque(rearRightWheel, brakeForce);
            return;
        }

        if (wheelCollidersBrake != null && wheelCollidersBrake.Length > 0)
        {
            foreach (WheelCollider wheelCollider in wheelCollidersBrake)
            {
                SetBrakeTorque(wheelCollider, 0f);
            }

            return;
        }

        SetBrakeTorque(frontLeftWheel, 0f);
        SetBrakeTorque(frontRightWheel, 0f);
        SetBrakeTorque(rearLeftWheel, 0f);
        SetBrakeTorque(rearRightWheel, 0f);
    }

    void SetBrakeTorque(WheelCollider wheel, float torque)
    {
        if (wheel)
        {
            wheel.brakeTorque = torque;
        }
    }

    void CacheDefaultFriction()
    {
        defaultForwardFrictions = new WheelFrictionCurve[cachedWheels.Length];
        defaultSidewaysFrictions = new WheelFrictionCurve[cachedWheels.Length];

        for (int i = 0; i < cachedWheels.Length; i++)
        {
            if (!cachedWheels[i])
            {
                continue;
            }

            defaultForwardFrictions[i] = cachedWheels[i].forwardFriction;
            defaultSidewaysFrictions[i] = cachedWheels[i].sidewaysFriction;
        }
    }

    void UpdateDriftPhysics()
    {
        if (!enableDrift)
        {
            driftBlend = Mathf.MoveTowards(driftBlend, 0f, driftGripChangeSpeed * Time.fixedDeltaTime);
            ApplyDriftFriction(driftBlend);
            return;
        }

        float speedFactor = Mathf.Clamp01(lastSpeedKmph / 60f);
        bool canDrift = lastSpeedKmph >= minimumDriftSpeedKmph;
        float targetDriftBlend = canDrift && isHandbrakeActive ? 1f : 0f;

        driftBlend = Mathf.MoveTowards(driftBlend, targetDriftBlend, driftGripChangeSpeed * Time.fixedDeltaTime);
        ApplyDriftFriction(driftBlend);

        if (isHandbrakeActive && canDrift && Mathf.Abs(lastSteerInput) > 0.05f)
        {
            rb.AddTorque(transform.up * lastSteerInput * driftYawAssist * speedFactor, ForceMode.Acceleration);
        }
    }

    void UpdateStabilityAssist()
    {
        isStabilityAssisting = false;

        if (!enableStabilityAssist || isHandbrakeActive)
        {
            return;
        }

        float slipAmount = Mathf.Abs(lastRearSidewaysSlip);
        float slipFactor = Mathf.InverseLerp(stabilitySlipThreshold, stabilitySlipThreshold * 2f, slipAmount);
        bool steerWasLimited = Mathf.Abs(lastEffectiveSteerInput) < Mathf.Abs(lastSteerInput) - 0.001f;

        if (slipFactor <= 0f && !steerWasLimited)
        {
            return;
        }

        Vector3 localAngularVelocity = transform.InverseTransformDirection(rb.angularVelocity);
        Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
        float assistFactor = Mathf.Max(slipFactor, steerWasLimited ? Mathf.Clamp01(lastSpeedKmph / highSpeedSteerAssistFullKmph) : 0f);

        rb.AddTorque(transform.up * -localAngularVelocity.y * stabilityYawDamping * assistFactor, ForceMode.Acceleration);
        rb.AddForce(transform.right * -localVelocity.x * stabilityLateralDamping * assistFactor, ForceMode.Acceleration);

        isStabilityAssisting = true;
    }

    void ApplyDriftFriction(float blend)
    {
        ApplyWheelFriction(2, driftRearForwardStiffness, driftRearSidewaysStiffness, blend);
        ApplyWheelFriction(3, driftRearForwardStiffness, driftRearSidewaysStiffness, blend);
    }

    void ApplyWheelFriction(int wheelIndex, float targetForwardStiffness, float targetSidewaysStiffness, float blend)
    {
        if (cachedWheels == null || wheelIndex < 0 || wheelIndex >= cachedWheels.Length || !cachedWheels[wheelIndex])
        {
            return;
        }

        WheelFrictionCurve forwardFriction = defaultForwardFrictions[wheelIndex];
        WheelFrictionCurve sidewaysFriction = defaultSidewaysFrictions[wheelIndex];

        forwardFriction.stiffness = Mathf.Lerp(defaultForwardFrictions[wheelIndex].stiffness, targetForwardStiffness, blend);
        sidewaysFriction.stiffness = Mathf.Lerp(defaultSidewaysFrictions[wheelIndex].stiffness, targetSidewaysStiffness, blend);

        cachedWheels[wheelIndex].forwardFriction = forwardFriction;
        cachedWheels[wheelIndex].sidewaysFriction = sidewaysFriction;
    }

    bool IsWheelSlipping(WheelCollider wheel)
    {
        WheelHit hit;
        return wheel.GetGroundHit(out hit) && Mathf.Abs(hit.sidewaysSlip) > 0.1f;
    }

    bool IsWheelDrifting(WheelCollider wheel)
    {
        WheelHit hit;
        return wheel.GetGroundHit(out hit) && Mathf.Abs(hit.forwardSlip) > 0.1f;
    }

    bool IsWheelBraking(WheelCollider wheel)
    {
        return wheel.isGrounded && Mathf.Abs(wheel.rpm) < 1f && wheel.brakeTorque > 0f;
    }

    float GetAverageRearSidewaysSlip()
    {
        float slip = 0f;
        int hits = 0;
        WheelHit hit;

        if (rearLeftWheel.GetGroundHit(out hit))
        {
            slip += hit.sidewaysSlip;
            hits++;
        }

        if (rearRightWheel.GetGroundHit(out hit))
        {
            slip += hit.sidewaysSlip;
            hits++;
        }

        return hits > 0 ? slip / hits : 0f;
    }

    void SetDustParticleSystemState(ParticleSystem dustParticleSystem, bool shouldPlay)
    {
        if (!dustParticleSystem)
        {
            return;
        }

        if (shouldPlay)
        {
            if (!dustParticleSystem.isPlaying)
            {
                dustParticleSystem.Play();
                EnableEmitter(true);

            }
        }
        else
        {
            if (dustParticleSystem.isPlaying)
            {
                dustParticleSystem.Stop();
                EnableEmitter(false);
            }
        }
    }

    private void EnableEmitter(bool state)
    {
        foreach (var trail in tireMarks)
        {
            if (trail)
            {
                trail.emitting = state;
            }
        }
    }

    void UpdateWheelPoses()
    {
        UpdateWheelPose(frontLeftWheel, frontLeftWheelTransform);
        UpdateWheelPose(frontRightWheel, frontRightWheelTransform, true);
        UpdateWheelPose(rearLeftWheel, rearLeftWheelTransform);
        UpdateWheelPose(rearRightWheel, rearRightWheelTransform, true);
    }

    void UpdateWheelPose(WheelCollider collider, Transform transform, bool flip = false)
    {
        Vector3 pos = transform.position;
        Quaternion quat = transform.rotation;

        collider.GetWorldPose(out pos, out quat);

        if (flip)
        {
            quat *= Quaternion.Euler(0, 180, 0);
        }

        transform.position = pos;
        transform.rotation = quat;
    }

#if UNITY_EDITOR
    void OnGUI()
    {
        if (!showDebugGui)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(debugGuiPosition.x, debugGuiPosition.y, 360f, 450f), GUI.skin.box);
        GUILayout.Label("Vehicle Debug");
        GUILayout.Label($"Speed: {lastSpeedKmph:0.0} km/h");
        GUILayout.Label($"Wheel speed: {lastWheelSpeedKmph:0.0} km/h");
        GUILayout.Label($"Driven wheel RPM: {lastDrivenWheelRPM:0}");
        GUILayout.Label($"Gear: {currentGear}/{(gearRatios != null ? gearRatios.Length : 0)}");
        GUILayout.Label($"Engine RPM: {lastEngineRPM:0} -> {lastTargetEngineRPM:0}");
        GUILayout.Label($"Throttle: {lastThrottleInput:0.00}");
        GUILayout.Label($"Steer: {lastSteerInput:0.00} -> {lastEffectiveSteerInput:0.00}");
        GUILayout.Label($"Torque: {lastAdjustedTorque:0}");
        GUILayout.Space(6f);
        GUILayout.Label($"Handbrake: {isHandbrakeActive}");
        GUILayout.Label($"Drifting: {isDrifting}");
        GUILayout.Label($"Stability assist: {isStabilityAssisting}");
        GUILayout.Label($"Drift blend: {driftBlend:0.00}");
        GUILayout.Label($"Rear sideways slip: {lastRearSidewaysSlip:0.000}");
        GUILayout.Label($"Rear brake torque: {rearLeftWheel.brakeTorque:0}/{rearRightWheel.brakeTorque:0}");
        GUILayout.Label($"Drive: {(enable4x4 ? "4x4" : "FWD")}");
        GUILayout.Space(6f);
        DrawWheelDebug("FL", frontLeftWheel);
        DrawWheelDebug("FR", frontRightWheel);
        DrawWheelDebug("RL", rearLeftWheel);
        DrawWheelDebug("RR", rearRightWheel);
        GUILayout.EndArea();
    }

    void DrawWheelDebug(string label, WheelCollider wheel)
    {
        if (!wheel)
        {
            GUILayout.Label($"{label}: missing");
            return;
        }

        WheelHit hit;
        if (wheel.GetGroundHit(out hit))
        {
            GUILayout.Label($"{label}: rpm {wheel.rpm:0}, fSlip {hit.forwardSlip:0.000}, sSlip {hit.sidewaysSlip:0.000}, brake {wheel.brakeTorque:0}");
        }
        else
        {
            GUILayout.Label($"{label}: air, rpm {wheel.rpm:0}, brake {wheel.brakeTorque:0}");
        }
    }
#endif
}
