using UnityEngine;
using UnityEngine.VFX;


public class CameraVFX : MonoBehaviour
{
	[SerializeField] private VisualEffect m_vfx;
	[SerializeField] private Camera m_camera;

	[SerializeField] private string m_paramNamePos = "StartAreaPos";
	[SerializeField] private string m_paramNameSize = "StartAreaSize";
	[SerializeField] private string m_paramNameRate = "PeriodicTimeMinMax";
	[SerializeField] private string m_paramNameRateCount = "PeriodicCountMinMax";
	[SerializeField] private string m_paramNameLifetimeMin = "SecondsMin";
	[SerializeField] private string m_paramNameLifetimeMax = "SecondsMax";
	[SerializeField] private Vector2 m_ratePerSizeMinMax = new(8.0f, 16.0f);
	[SerializeField] private Vector2 m_lifetimePerSizeMinMax = new(0.05f, 0.1f);


	private int m_paramIDPos;
	private int m_paramIDSize;
	private int m_paramIDRate;
	private int m_paramIDRateCount;
	private int m_paramIDLifetimeMin;
	private int m_paramIDLifetimeMax;

	private float m_cameraSize = -1.0f;


	private void Start()
	{
		m_paramIDPos = Shader.PropertyToID(m_paramNamePos);
		m_paramIDSize = Shader.PropertyToID(m_paramNameSize);
		m_paramIDRate = Shader.PropertyToID(m_paramNameRate);
		m_paramIDRateCount = Shader.PropertyToID(m_paramNameRateCount);
		m_paramIDLifetimeMin = Shader.PropertyToID(m_paramNameLifetimeMin);
		m_paramIDLifetimeMax = Shader.PropertyToID(m_paramNameLifetimeMax);
	}

	private void Update() // TODO: only when camera transform changes?
	{
		if (m_cameraSize == m_camera.orthographicSize) // TODO: also check for camera/VFX relative position changes?
		{
			return;
		}
		m_cameraSize = m_camera.orthographicSize;

		Vector3 spawnPos = m_camera.transform.position - m_vfx.transform.position;
		spawnPos.y += m_cameraSize + 1.0f; // TODO: parameterize y-buffer?
		spawnPos.z = 0.0f; // TODO: don't lock z to local zero?
		m_vfx.SetVector3(m_paramIDPos, spawnPos);

		Vector3 size = new(m_cameraSize * 2.0f * m_camera.aspect, 0.0f); // TODO: parameterize y-size?
		m_vfx.SetVector3(m_paramIDSize, size);
		Vector2 rate = Vector2.one / (m_ratePerSizeMinMax * m_cameraSize);
		m_vfx.SetVector2(m_paramIDRate, rate);
		Vector2 rateCount = rate.x >= Time.deltaTime ? Vector2.one : new Vector2(Time.deltaTime, Time.deltaTime) / rate;
		m_vfx.SetVector2(m_paramIDRateCount, rateCount);
		Vector2 lifetime = m_lifetimePerSizeMinMax * m_cameraSize;
		m_vfx.SetFloat(m_paramIDLifetimeMin, lifetime.x);
		m_vfx.SetFloat(m_paramIDLifetimeMax, lifetime.y);
	}
}
