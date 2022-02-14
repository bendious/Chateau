using UnityEngine;


public sealed class ArmController : MonoBehaviour, IHolder
{
	public /*override*/ int HoldCountMax => 1;

	public Vector3 m_offset;
	public /*override*/ Vector3 AttachOffsetLocal => m_offset;
	public /*override*/ Vector3 ChildAttachPointLocal => Vector3.right * GetComponent<SpriteRenderer>().sprite.bounds.size.x;


	private float m_aimSpringStiffness = 100.0f;
	private float m_aimSpringDampPct = 0.5f;

	private float m_swingDegreesPerSec;
	private float m_swingRadiusPerSec;
	private float m_radiusSpringStiffness;
	private float m_radiusSpringDampPct;


	public float Speed => Mathf.Abs(m_aimVelocityArm + m_aimVelocityItem) + Mathf.Abs(m_aimRadiusVelocity); // TODO: incorporate aim velocity directions?


	private bool LeftFacing => Mathf.Cos(Mathf.Deg2Rad * m_aimDegreesArm) < 0.0f; // TODO: efficiency?



	private float m_aimDegreesArm;
	private float m_aimDegreesItem;
	private float m_aimVelocityArm;
	private float m_aimVelocityItem;
	private float m_aimRadius;
	private float m_aimRadiusVelocity;
	private bool m_swingDirection;


	public /*override*/ bool ItemAttach(ItemController item)
	{
		bool attached = IHolder.ItemAttachInternal(item, this);
		if (!attached)
		{
			return false;
		}

		m_aimSpringStiffness = item.m_aimSpringStiffness; // TODO: reset when detaching?
		m_aimSpringDampPct = item.m_aimSpringDampPct;

		m_aimDegreesArm = AimDegreesRaw(transform.parent.position, Vector2.zero, item.transform.position); // TODO: lerp? use most recent rootOffset?
		m_aimVelocityArm = 0.0f;
		m_aimDegreesItem = item.transform.rotation.eulerAngles.z - m_aimDegreesArm;
		m_aimVelocityItem = 0.0f;
		m_aimRadiusVelocity = 0.0f;

		return true;
	}

	public /*override*/ void ItemDetach(ItemController item, bool noAutoReplace)
	{
		IHolder.ItemDetachInternal(item, this, noAutoReplace);
	}


	public void Swing(float swingDegreesPerSec, float swingRadiusPerSec, float radiusSpringStiffness, float radiusSpringDampPct)
	{
		m_swingDegreesPerSec = swingDegreesPerSec;
		m_swingRadiusPerSec = swingRadiusPerSec;
		m_radiusSpringStiffness = radiusSpringStiffness;
		m_radiusSpringDampPct = radiusSpringDampPct;

		m_aimVelocityArm += m_swingDirection ? m_swingDegreesPerSec : -m_swingDegreesPerSec;
		m_aimRadiusVelocity += m_swingRadiusPerSec;
		m_swingDirection = !m_swingDirection;
	}

	public void UpdateAim(Vector2 rootOffset, Vector2 aimPositionArm, Vector2 aimPositionItem)
	{
		m_aimDegreesArm = DampedSpring(m_aimDegreesArm, AimDegreesRaw(transform.parent.position, rootOffset, aimPositionArm), m_aimSpringDampPct, true, m_aimSpringStiffness, ref m_aimVelocityArm);
		m_aimRadius = DampedSpring(m_aimRadius, 0.0f, m_radiusSpringDampPct, false, m_radiusSpringStiffness, ref m_aimRadiusVelocity);

		transform.localRotation = Quaternion.Euler(0.0f, 0.0f, m_aimDegreesArm);
		Vector3 localPos = (Vector3)rootOffset + (LeftFacing ? new Vector3(m_offset.x, m_offset.y, -m_offset.z) : m_offset) + transform.localRotation * Vector3.right * m_aimRadius;
		transform.localPosition = localPos;

		bool leftFacingCached = LeftFacing;
		foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>())
		{
			renderer.flipY = leftFacingCached;
			if (renderer.gameObject != gameObject && !renderer.GetComponent<ItemController>().IsSwinging)
			{
				m_aimDegreesItem = DampedSpring(m_aimDegreesItem, AimDegreesRaw(renderer.transform.position, Vector2.zero, aimPositionItem) - m_aimDegreesArm, m_aimSpringDampPct, true, m_aimSpringStiffness, ref m_aimVelocityItem);
				renderer.transform.localRotation = Quaternion.Euler(0.0f, 0.0f, m_aimDegreesItem);
			}
		}
	}


	private float AimDegreesRaw(Vector2 rootPos, Vector2 rootOffset, Vector2 aimPosition)
	{
		Vector2 aimDiff = aimPosition - (rootPos + rootOffset + (Vector2)m_offset);
		return Mathf.Rad2Deg * Mathf.Atan2(aimDiff.y, aimDiff.x);
	}

	private float DampedSpring(float current, float target, float dampPct, bool isAngle, float stiffness, ref float velocityCurrent)
	{
		// spring motion: F = kx - dv, where x = {vel/pos}_desired - {vel/pos}_current
		// critically damped spring: d = 2*sqrt(km)
		ItemController item = GetComponentInChildren<ItemController>();
		float mass = item == null ? 0.2f : item.GetComponent<Rigidbody2D>().mass; // TODO: expose arm mass?
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
