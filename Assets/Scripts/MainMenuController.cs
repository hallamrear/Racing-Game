using UnityEditor.ShaderGraph.Internal;
using UnityEngine;

public class MainMenuController : MonoBehaviour
{
    [SerializeField]
    private Transform m_Target;
    [SerializeField]
    private float m_RotationSpeed;
    Camera m_Camera;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        m_Camera = Camera.main;

        if(m_Camera != null && m_Target != null)
        {
            m_Camera.transform.LookAt(m_Target);
        }
    }

    private void FixedUpdate()
    {
        if (m_Target != null)
        {
            m_Target.RotateAround(Vector3.zero, Vector3.up, m_RotationSpeed * Time.fixedDeltaTime);
        }
    }
}
