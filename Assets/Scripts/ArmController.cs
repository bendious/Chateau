using UnityEngine;
using UnityEngine.Assertions;


public class ArmController : MonoBehaviour
{
	private float m_aimSpringStiffness = 100.0f;
	private float m_aimSpringDampPct = 0.5f;

	private float m_swingDegreesPerSec;
	private float m_swingRadiusPerSec;
	private float m_radiusSpringStiffness;
	private float m_radiusSpringDampPct;


	public float Speed => Mathf.Abs(m_aimVelocity) + Mathf.Abs(m_aimRadiusVelocity); // TODO: incorporate aim velocity direction?


	private bool LeftFacing => Mathf.Cos(Mathf.Deg2Rad * m_aimDegrees) < 0.0f; // TODO: efficiency?



	private float m_aimDegrees;
	private float m_aimVelocity;
	private float m_aimRadius;
	private float m_aimRadiusVelocity;
	private bool m_swingDirection;


	public void OnItemAttachment(ItemController item)
	{
		m_aimSpringStiffness = item.m_aimSpringStiffness; // TODO: reset when detaching?
		m_aimSpringDampPct = item.m_aimSpringDampPct;
		m_aimDegrees = AimDegreesRaw(item.transform.position); // TODO: lerp?
		m_aimVelocity = 0.0f;
		m_aimRadiusVelocity = 0.0f;
	}

	public void Swing(float swingDegreesPerSec, float swingRadiusPerSec, float radiusSpringStiffness, float radiusSpringDampPct)
	{
		m_swingDegreesPerSec = swingDegreesPerSec;
		m_swingRadiusPerSec = swingRadiusPerSec;
		m_radiusSpringStiffness = radiusSpringStiffness;
		m_radiusSpringDampPct = radiusSpringDampPct;

		m_aimVelocity += m_swingDirection ? m_swingDegreesPerSec : -m_swingDegreesPerSec;
		m_aimRadiusVelocity += m_swingRadiusPerSec;
		m_swingDirection = !m_swingDirection;
	}

	public void UpdateAim(Vector2 position, float radius)
	{
		m_aimDegrees = DampedSpring(m_aimDegrees, AimDegreesRaw(position), m_aimSpringDampPct, true, m_aimSpringStiffness, ref m_aimVelocity);
		m_aimRadius = DampedSpring(m_aimRadius, radius, m_radiusSpringDampPct, false, m_radiusSpringStiffness, ref m_aimRadiusVelocity);

		transform.localRotation = Quaternion.Euler(0.0f, 0.0f, m_aimDegrees);
		Vector3 localPos = transform.localRotation * Vector3.right * m_aimRadius;
		localPos.z = transform.localPosition.z; // NOTE that Z is used to force rendering on top of or behind our parent
		transform.localPosition = localPos;

		bool leftFacingCached = LeftFacing;
		foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>())
		{
			renderer.flipY = leftFacingCached;
		}
	}


	private float AimDegreesRaw(Vector2 position)
	{
		Vector2 aimDiff = position - (Vector2)transform.parent.position;
		return Mathf.Rad2Deg * Mathf.Atan2(aimDiff.y, aimDiff.x);
	}

	private float DampedSpring(float current, float target, float dampPct, bool isAngle, float stiffness, ref float velocityCurrent)
	{
		// spring motion: F = kx - dv, where x = {vel/pos}_desired - {vel/pos}_current
		// critically damped spring: d = 2*sqrt(km)
		float mass = 0.2f;//TODO //m_body.mass;
		float dampingFactor = 2.0f * Mathf.Sqrt(m_aimSpringStiffness * mass) * dampPct;
		float diff = target - current;
		while (isAngle && Mathf.Abs(diff) > 180.0f)
		{
			diff -= diff < 0.0f ? -360.0f : 360.0f;
		}
		float force = stiffness * diff - dampingFactor * velocityCurrent;

		float accel = force / mass;
		velocityCurrent += accel * Time.fixedDeltaTime;

		return current + velocityCurrent * Time.fixedDeltaTime;
	}
}
