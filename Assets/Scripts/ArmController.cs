using System.Linq;
using UnityEngine;


[RequireComponent(typeof(Collider2D))]
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
		m_damageThresholdSpeed = 4.0f,
		m_damage = 0.1f
	};


	private SwingInfo m_swingInfoCur;


	public float Speed => Mathf.Abs(Mathf.Deg2Rad * (m_aimVelocityArm + m_aimVelocityItem) * ((Vector2)transform.parent.position - (Vector2)transform.position).magnitude) + Mathf.Abs(m_aimRadiusVelocity); // NOTE the conversion from angular velocity to linear speed via arclength=radians*radius // TODO: incorporate aim velocity directions?


	private bool LeftFacing => Mathf.Cos(Mathf.Deg2Rad * m_aimDegreesArm) < 0.0f; // TODO: efficiency?



	private Collider2D[] m_colliders;

	private float m_aimDegreesArm;
	private float m_aimDegreesItem;
	private float m_aimVelocityArm;
	private float m_aimVelocityItem;
	private float m_aimRadius;
	private float m_aimRadiusVelocity;
	private bool m_swingDirection;


	private void Awake()
	{
		m_swingInfoCur = m_swingInfoDefault;

		m_colliders = GetComponents<Collider2D>();
		Collider2D parentCollider = transform.parent.GetComponent<Collider2D>();
		foreach (Collider2D collider in m_colliders)
		{
			Physics2D.IgnoreCollision(parentCollider, collider);
		}
	}

	// TODO: combine w/ ItemController version?
	private void OnCollisionEnter2D(Collision2D collision)
	{
		KinematicObject kinematicObj = collision.gameObject.GetComponent<KinematicObject>();
		Rigidbody2D body = m_colliders.First().attachedRigidbody;
		if (kinematicObj != null && kinematicObj.ShouldIgnore(body, m_colliders, false, false, false))
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
				otherHealth.Decrement(gameObject, m_swingInfoCur.m_damage);
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
		foreach (Collider2D collider in m_colliders)
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
		m_swingInfoCur = m_swingInfoDefault; // NOTE that this has to be BEFORE ItemDetachInternal(), which might re-attach a replacement w/ a non-default SwingInfo
		IHolder.ItemDetachInternal(item, this, noAutoReplace);
		foreach (Collider2D collider in m_colliders)
		{
			collider.enabled = true;
		}
		m_aimVelocityItem = 0.0f;
	}


	public void Swing()
	{
		m_aimVelocityArm += m_swingDirection ? m_swingInfoCur.m_degreesPerSec : -m_swingInfoCur.m_degreesPerSec;
		m_aimRadiusVelocity += m_swingInfoCur.m_radiusPerSec;
		m_swingDirection = !m_swingDirection;

		// play audio if not holding anything (any held item will play audio for itself)
		if (GetComponentInChildren<ItemController>() == null)
		{
			GetComponent<AudioSource>().PlayOneShot(GameController.Instance.m_materialSystem.Find(m_colliders.First().sharedMaterial).RandomMovementAudio()); // TODO: don't assume first collider is main material?
		}
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
