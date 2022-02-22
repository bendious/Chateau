using UnityEngine;


[RequireComponent(typeof(Collider2D), typeof(Rigidbody2D))] // NOTE that the rigid body is used only to keep the collider from being aggregated into the character's rigid body
public sealed class ArmController : MonoBehaviour, IHolder
{
	public /*override*/ int HoldCountMax => 1;

	public Vector3 m_offset;
	public /*override*/ Vector3 AttachOffsetLocal => m_offset;
	public /*override*/ Vector3 ChildAttachPointLocal => Vector3.right * GetComponent<SpriteRenderer>().sprite.bounds.size.x;

	public SwingInfo m_swingInfoDefault = new() {
		m_degreesPerSec = 5000.0f,
		m_radiusPerSec = 1.0f,
		m_aimSpringStiffness = 100.0f,
		m_aimSpringDampPct = 0.5f,
		m_radiusSpringStiffness = 50.0f,
		m_radiusSpringDampPct = 0.75f,
		m_damageThresholdSpeed = 2.0f,
		m_damage = 0.1f
	};


	private SwingInfo m_swingInfoCur;


	public float Speed => Mathf.Abs(m_aimVelocityArm + m_aimVelocityItem) + Mathf.Abs(m_aimRadiusVelocity); // TODO: incorporate aim velocity directions?


	private bool LeftFacing => Mathf.Cos(Mathf.Deg2Rad * m_aimDegreesArm) < 0.0f; // TODO: efficiency?



	private float m_aimDegreesArm;
	private float m_aimDegreesItem;
	private float m_aimVelocityArm;
	private float m_aimVelocityItem;
	private float m_aimRadius;
	private float m_aimRadiusVelocity;
	private bool m_swingDirection;


	private void Start()
	{
		m_swingInfoCur = m_swingInfoDefault;
		Physics2D.IgnoreCollision(transform.parent.GetComponent<Collider2D>(), GetComponent<Collider2D>()); // TODO: handle multi-colliders?
	}

	// TODO: combine w/ ItemController version?
	private void OnCollisionEnter2D(Collision2D collision)
	{
		KinematicObject kinematicObj = collision.gameObject.GetComponent<KinematicObject>();
		Rigidbody2D body = GetComponent<Rigidbody2D>();
		if (kinematicObj != null && kinematicObj.ShouldIgnore(body, GetComponents<Collider2D>(), false, false, false))
		{
			return;
		}

		if (!transform.parent.GetComponent<KinematicCharacter>().CanDamage(collision.gameObject))
		{
			return;
		}

		// check speed
		float collisionSpeed = (kinematicObj == null ? collision.relativeVelocity.magnitude : (body.velocity - kinematicObj.velocity).magnitude) + Speed;
		if (collisionSpeed > m_swingInfoCur.m_damageThresholdSpeed)
		{
			Health otherHealth = collision.gameObject.GetComponent<Health>();
			if (otherHealth != null)
			{
				otherHealth.Decrement(gameObject, m_swingInfoCur.m_damage); // TODO: round if damaging avatar?
			}
		}
	}


	public /*override*/ bool ItemAttach(ItemController item)
	{
		bool attached = IHolder.ItemAttachInternal(item, this);
		if (!attached)
		{
			return false;
		}

		m_swingInfoCur = item.m_swingInfo;
		foreach (Collider2D collider in GetComponents<Collider2D>())
		{
			collider.enabled = false;
		}

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
		m_swingInfoCur = m_swingInfoDefault;
		foreach (Collider2D collider in GetComponents<Collider2D>())
		{
			collider.enabled = true;
		}
	}


	public void Swing()
	{
		m_aimVelocityArm += m_swingDirection ? m_swingInfoCur.m_degreesPerSec : -m_swingInfoCur.m_degreesPerSec;
		m_aimRadiusVelocity += m_swingInfoCur.m_radiusPerSec;
		m_swingDirection = !m_swingDirection;
	}

	public void UpdateAim(Vector2 rootOffset, Vector2 aimPositionArm, Vector2 aimPositionItem)
	{
		m_aimDegreesArm = DampedSpring(m_aimDegreesArm, AimDegreesRaw(transform.parent.position, rootOffset, aimPositionArm), m_swingInfoCur.m_aimSpringDampPct, true, m_swingInfoCur.m_aimSpringStiffness, ref m_aimVelocityArm);
		m_aimRadius = DampedSpring(m_aimRadius, 0.0f, m_swingInfoCur.m_radiusSpringDampPct, false, m_swingInfoCur.m_radiusSpringStiffness, ref m_aimRadiusVelocity);

		transform.localRotation = Quaternion.Euler(0.0f, 0.0f, m_aimDegreesArm);
		Vector3 localPos = (Vector3)rootOffset + (LeftFacing ? new Vector3(m_offset.x, m_offset.y, -m_offset.z) : m_offset) + transform.localRotation * Vector3.right * m_aimRadius;
		transform.localPosition = localPos;

		bool leftFacingCached = LeftFacing;
		foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>())
		{
			renderer.flipY = leftFacingCached;
			if (renderer.gameObject != gameObject && !renderer.GetComponent<ItemController>().IsSwinging)
			{
				m_aimDegreesItem = DampedSpring(m_aimDegreesItem, AimDegreesRaw(renderer.transform.position, Vector2.zero, aimPositionItem) - m_aimDegreesArm, m_swingInfoCur.m_aimSpringDampPct, true, m_swingInfoCur.m_aimSpringStiffness, ref m_aimVelocityItem);
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
		float dampingFactor = 2.0f * Mathf.Sqrt(m_swingInfoCur.m_aimSpringStiffness * mass) * dampPct;
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
