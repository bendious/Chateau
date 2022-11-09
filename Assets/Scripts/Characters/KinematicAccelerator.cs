using UnityEngine;


public class KinematicAccelerator : KinematicObject
{
	public Component m_target;

	public Vector2 m_startOffset;

	[SerializeField] private Vector2 m_accelerationMag;


	private Vector2 m_acceleration;


	protected override void Start()
	{
		base.Start();

		m_acceleration.x = m_target.transform.position.x <= transform.position.x ? -m_accelerationMag.x : m_accelerationMag.x;
		m_acceleration.y = m_target.transform.position.y <= transform.position.y ? -m_accelerationMag.y : m_accelerationMag.y;
	}


	protected override void ComputeVelocity()
	{
		TargetVelocity = velocity + m_acceleration * Time.deltaTime; // TODO: more nuance?
	}
}
