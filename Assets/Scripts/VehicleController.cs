using System.Collections.Generic;
using TMPro;
using Unity.Collections.LowLevel.Unsafe;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class VehicleController : MonoBehaviour
{
    private Rigidbody m_Rigidbody;
    [Header("Movement and Suspension"), SerializeField]
    private GameObject[] m_WheelCorners;
    [SerializeField]
    private GameObject[] m_TurningWheels;
    [SerializeField]
    private float m_WheelTurnRate;
    [SerializeField]
    private float m_SpringRestDistance;
    [SerializeField]
    private float m_SpringTravelDistance;
    [SerializeField]
    private float m_SpringDamping;
    [SerializeField]
    private float m_SpringStrength;
    private float m_SteeringInput;
    private float m_AccelerationInput;
    private float m_BrakingInput;
    [SerializeField]
    private float m_MaxSteeringAngle = 30;
    private float m_SteeringAngle = 0.0f;
    [SerializeField]
    private float m_WheelRadius = 2.0f;
    float m_MaximumLength;

    Vector3 m_ForceSum;

    [Header("Speed and Power"), SerializeField]
    private float m_Speed;
    [SerializeField] AnimationCurve m_TorqueCurve;

    [Header("Tires and Grip"), SerializeField, UnityEngine.Range(0.0f, 1.0f)]
    private float m_tireGripScale;
    [SerializeField] private float m_tireMass;
    [SerializeField] private bool m_rotateWheels;
    [SerializeField] GameObject[] m_tireMeshes;

    [SerializeField]
    LayerMask m_SuspensionLayerMask;

    Vector3 m_SuspensionForce;
    Vector3 m_TractionForce;
    Vector3 m_LateralForce;
    float m_CurrentSlipRatio;
    float m_CurrentSlipAngle;
    PlayerInput m_PlayerInput;

    [Header("Torque Curve")]
    [SerializeField] float m_TorqueCurve_a;
    [SerializeField] float m_TorqueCurve_b;
    [SerializeField] float m_TorqueCurve_c;
    [SerializeField] float m_TorqueCurve_d;
    [SerializeField] float m_TorqueCurve_f;

    [Header("Gearing")]
    [SerializeField] List<float> m_GearRatios;

    [Header("Engine")]
    private float m_CurrentRPM;

    void Start()
    {
        m_CurrentRPM = 0.0f;
        m_SuspensionForce = Vector3.zero;
        m_TractionForce = Vector3.zero;
        m_LateralForce = Vector3.zero;
        m_CurrentSlipRatio = 0.0f;
        m_CurrentSlipAngle = 0.0f;
        m_ForceSum = Vector3.zero;
        m_SteeringAngle = 0.0f;
        m_Rigidbody = GetComponent<Rigidbody>();
        m_PlayerInput = GetComponent<PlayerInput>();

        m_PlayerInput.actions["Move"].performed += OnSteeringInputPressed;
        m_PlayerInput.actions["Move"].canceled += OnSteeringInputCancelled;
        m_PlayerInput.actions["Accelerate"].performed += OnAccelerationInputPressed;
        m_PlayerInput.actions["Accelerate"].canceled += OnAccelerationInputCancelled;
        m_PlayerInput.actions["Brake"].performed += OnBrakingInputPressed;
        m_PlayerInput.actions["Brake"].canceled += OnBrakingInputCancelled;
    }

    public void OnSteeringInputPressed(InputAction.CallbackContext ctx)
    {
        m_SteeringInput = ctx.ReadValue<Vector2>().x;
    }

    public void OnSteeringInputCancelled(InputAction.CallbackContext ctx)
    {
        m_SteeringInput = 0.0f;
    }
    public void OnAccelerationInputPressed(InputAction.CallbackContext ctx)
    {
        if (m_BrakingInput <= float.Epsilon)
        {
            m_AccelerationInput = ctx.ReadValue<float>();
        }
    }

    public void OnAccelerationInputCancelled(InputAction.CallbackContext ctx)
    {
        m_AccelerationInput = 0.0f;
    }

    public void OnBrakingInputPressed(InputAction.CallbackContext ctx)
    {
        m_AccelerationInput = ctx.ReadValue<float>() * -1.0f;
    }

    public void OnBrakingInputCancelled(InputAction.CallbackContext ctx)
    {
        m_AccelerationInput = 0.0f;
    }

    private void OnDrawGizmos()
    {
        if (GetComponent<Rigidbody>())
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(GetComponent<Rigidbody>().worldCenterOfMass, 0.5f);
        }
    }

    public void ZeroInputVector()
    {
        m_SteeringInput = 0.0f;
        m_AccelerationInput = 0.0f;
    }

    float GetTorqueCurveApproximation()
    {
        float e = Mathf.Exp(1f);
        float x = 0.0f; //current speed;

        float numerator = 0.0f - Mathf.Pow(m_TorqueCurve_c * (x - m_TorqueCurve_d), 2.0f);
        float fSqr = m_TorqueCurve_f * m_TorqueCurve_f;
        float division = (numerator / fSqr);

        return (m_TorqueCurve_a - m_TorqueCurve_b) * e * division + m_TorqueCurve_b;
    }


    private float CalculateEngineRPMDifferentialEquation()
    {
        float m_EngineFrictionForce = 0.0f;
        float m_EngineBrakingForce = 0.0f;
        float m_ExpectedRPMFromWheel = 0.0f;
        float k0T = GetTorqueCurveApproximation() * m_AccelerationInput;
        float k1Rd = m_EngineBrakingForce * (m_ExpectedRPMFromWheel - m_CurrentRPM);
        float k2Rt = m_EngineFrictionForce * m_CurrentRPM;
        return (k0T + k1Rd - k2Rt);
    }

    private void WheelAngularVelocityDifferentialEquation()
    {
        float m_ExpectedAngularVelocityFromCurrentRPM = 0.0f;
        float m_CurrentAngularVelocity = 0.0f;
        float m_BrakingForceScalar = 0.0f;
        float m_RoadFriction = 0.0f;
        float k0AVd = GetTorqueCurveApproximation() * (m_ExpectedAngularVelocityFromCurrentRPM - m_CurrentAngularVelocity);
        float k1B = m_BrakingForceScalar * m_BrakingInput;
        float k2AV = m_RoadFriction * m_CurrentAngularVelocity;
    }

    private float GetPacejkaValue(float longitudinalSlipRatio)
    {
        float m_Pacejka_d = 1.0f;
        float m_Pacejka_c = 2.6f;
        float m_Pacejka_e = 0.8f;
        return m_Pacejka_d * (Mathf.Sin(m_Pacejka_c * Mathf.Atan(longitudinalSlipRatio - m_Pacejka_e * (longitudinalSlipRatio - Mathf.Atan(longitudinalSlipRatio)))));
    }

    private float GetTractionForce(bool isWheelTouching, float wheelAngularVelocity, float wheelLinearVelocity)
    {
        var contactVelocity = wheelAngularVelocity * m_WheelRadius;
        var rollingVelocity = wheelLinearVelocity / m_WheelRadius;
        float slip = contactVelocity - rollingVelocity;
        float slipRatio = 0.0f;

        if(isWheelTouching)
        {
            slipRatio = (contactVelocity * m_WheelRadius) / rollingVelocity - 1;
        }

        float tractionForce = GetPacejkaValue(slipRatio);

        return tractionForce;
    }


    private void UpdateEngine()
    {
        m_CurrentRPM += CalculateEngineRPMDifferentialEquation();
    }

    private void UpdateChassis()
    {
        m_ForceSum = (m_SuspensionForce) + (m_TractionForce * m_CurrentSlipRatio) + (m_LateralForce * m_CurrentSlipAngle);


        if (m_Rigidbody && m_ForceSum.sqrMagnitude > 0.001f)
        {
            m_Rigidbody.AddForce(m_ForceSum);
        }
    }


    private void FixedUpdate()
    {
        Debug.Log(m_CurrentRPM);
        m_ForceSum = Vector3.zero;

        UpdateEngine();

        m_MaximumLength = m_SpringRestDistance + m_SpringTravelDistance;

        if (Mathf.Abs(m_SteeringInput) > 0.0f)
        {
            m_SteeringAngle += (m_SteeringInput * m_WheelTurnRate);
            m_SteeringAngle = Mathf.Clamp(m_SteeringAngle, -m_MaxSteeringAngle, m_MaxSteeringAngle);
        }

        if (m_rotateWheels)
        {
            float currentSpeed = Vector3.Dot(transform.forward, m_Rigidbody.linearVelocity);

            if (m_TurningWheels != null)
            {
                for (int i = 0; i < m_TurningWheels.Length; i++)
                {
                    m_TurningWheels[i].transform.rotation = transform.rotation * Quaternion.Euler(0.0f, m_SteeringAngle, 0.0f);
                }
            }

            if (m_tireMeshes != null)
            {
                for (int i = 0; i < m_TurningWheels.Length; i++)
                {
                    m_tireMeshes[i].transform.rotation = m_TurningWheels[i].transform.rotation;
                }
            }
        }

        Ray ray = new Ray();

        for (int i = 0; i < m_WheelCorners.Length; i++)
        {
            Vector3 cornerDown = (m_WheelCorners[i].transform.up * -1.0f);
            ray.origin = m_WheelCorners[i].transform.position;
            ray.direction = cornerDown;

            bool didWheelRaycastHit = Physics.Raycast(ray, out RaycastHit tireRaycast, m_MaximumLength, m_SuspensionLayerMask);

            //Suspension Force
            if (didWheelRaycastHit)
            {
                float currentSpringLength = tireRaycast.distance - m_WheelRadius;
                float springCompressed = Mathf.Clamp01((m_SpringRestDistance - currentSpringLength) / m_SpringTravelDistance);

                float springVelocity = Vector3.Dot(m_Rigidbody.GetPointVelocity(m_WheelCorners[i].transform.position), m_WheelCorners[i].transform.up);

                float force = (springCompressed * m_SpringStrength) - (springVelocity * m_SpringDamping);

                m_Rigidbody.AddForceAtPosition(m_WheelCorners[i].transform.up * force, m_WheelCorners[i].transform.position);

                Debug.DrawLine(m_WheelCorners[i].transform.position, tireRaycast.point, Color.green);
                Debug.DrawLine(tireRaycast.point, m_WheelCorners[i].transform.position + (m_MaximumLength * -m_WheelCorners[i].transform.up), Color.red);

                m_tireMeshes[i].transform.position = tireRaycast.point + (m_WheelCorners[i].transform.up * m_WheelRadius);
            }
            else
            {
                Debug.DrawLine(m_WheelCorners[i].transform.position, m_WheelCorners[i].transform.position + (m_MaximumLength * -m_WheelCorners[i].transform.up), Color.green);
                m_tireMeshes[i].transform.position = m_WheelCorners[i].transform.position + (m_MaximumLength * -m_WheelCorners[i].transform.up) + (m_WheelCorners[i].transform.up * m_WheelRadius);
            }
        }
    }
}