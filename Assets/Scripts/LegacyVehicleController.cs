using UnityEngine;
using UnityEngine.InputSystem;


[RequireComponent(typeof(Rigidbody))]
public class LegacyVehicleController : MonoBehaviour
{
    private Rigidbody m_rigidbody;
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

    PlayerInput m_PlayerInput;

    void Start()
    {
        m_SteeringAngle = 0.0f;
        m_rigidbody = GetComponent<Rigidbody>();

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

    private void FixedUpdate()
    {
        m_MaximumLength = m_SpringRestDistance + m_SpringTravelDistance;

        if (Mathf.Abs(m_SteeringInput) > 0.0f)
        {
            m_SteeringAngle += (m_SteeringInput * m_WheelTurnRate);
            m_SteeringAngle = Mathf.Clamp(m_SteeringAngle, -m_MaxSteeringAngle, m_MaxSteeringAngle);
        }

        if (m_rotateWheels)
        {
            float currentSpeed = Vector3.Dot(transform.forward, m_rigidbody.linearVelocity);

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

                float springVelocity = Vector3.Dot(m_rigidbody.GetPointVelocity(m_WheelCorners[i].transform.position), m_WheelCorners[i].transform.up);

                float force = (springCompressed * m_SpringStrength) - (springVelocity * m_SpringDamping);

                m_rigidbody.AddForceAtPosition(m_WheelCorners[i].transform.up * force, m_WheelCorners[i].transform.position);

                Debug.DrawLine(m_WheelCorners[i].transform.position, tireRaycast.point, Color.green);
                Debug.DrawLine(tireRaycast.point, m_WheelCorners[i].transform.position + (m_MaximumLength * -m_WheelCorners[i].transform.up), Color.red);

                m_tireMeshes[i].transform.position = tireRaycast.point + (m_WheelCorners[i].transform.up * m_WheelRadius);
            }
            else
            {
                Debug.DrawLine(m_WheelCorners[i].transform.position, m_WheelCorners[i].transform.position + (m_MaximumLength * -m_WheelCorners[i].transform.up), Color.green);
                m_tireMeshes[i].transform.position = m_WheelCorners[i].transform.position + (m_MaximumLength * -m_WheelCorners[i].transform.up) + (m_WheelCorners[i].transform.up * m_WheelRadius);
            }

            //Steering
            if (didWheelRaycastHit)
            {
                Vector3 steeringDirection = m_WheelCorners[i].transform.right;

                Vector3 tireVelocity = m_rigidbody.GetPointVelocity(m_WheelCorners[i].transform.position);
                //Projecting velocity onto direction to get speed.
                float steeringSpeed = Vector3.Dot(steeringDirection, tireVelocity);

                //Grip scale should be 0-1 with 0 being no grip and 1 being full grip.
                float desiredVelocity = -steeringSpeed * m_tireGripScale;

                //Calculate desired acceleration change for 1 fixed step using accel = dt / t.
                float desiredAcceleration = desiredVelocity / Time.fixedDeltaTime;

                //Calculate steer force using f=ma.
                Vector3 steeringForce = steeringDirection * m_tireMass * desiredAcceleration;

                m_rigidbody.AddForceAtPosition(steeringForce, m_WheelCorners[i].transform.position);

                Debug.DrawLine(m_WheelCorners[i].transform.position, m_WheelCorners[i].transform.position + steeringForce, Color.red);
            }

            //Acceleration
            if (didWheelRaycastHit)
            {
                Vector3 tireDirection = m_WheelCorners[i].transform.forward;

                if (Mathf.Abs(m_AccelerationInput) > float.Epsilon)
                {
                    //Get speed of rigidbody in travel direction.
                    float currentSpeed = Vector3.Dot(transform.forward, m_rigidbody.linearVelocity);

                    float normalisedSpeed = Mathf.Clamp01(Mathf.Abs(currentSpeed) / m_Speed);

                    //Get torque from curve.
                    float torqueCurve = m_TorqueCurve.Evaluate(normalisedSpeed);
                    float torque = m_Speed * torqueCurve * m_AccelerationInput;

                    Vector3 accelerationForce = tireDirection * torque;
                    m_rigidbody.AddForceAtPosition(accelerationForce, m_WheelCorners[i].transform.position);

                    Debug.DrawLine(m_WheelCorners[i].transform.position, m_WheelCorners[i].transform.position + accelerationForce, Color.blue);
                }
            }
        }
    }
}