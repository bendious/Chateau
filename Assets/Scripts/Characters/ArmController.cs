using System.Linq;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public sealed class ArmController : MonoBehaviour, IHolder
{
	public /*override*/ int HoldCountMax => 1;

	public Vector3 m_offset;
	public /*override*/ Vector3 AttachOffsetLocal => m_offset;
	public /*override*/ Vector3 ChildAttachPointLocal => Vector3.right * GetComponent<SpriteRenderer>().sprite.bounds.size.x;

	public SwingInfo m_swingInfoDefault = new() {
		m_angularNewtonmeters = 300.0f,
		m_linearNewtons = 0.1f,
		m_aimSpringDampPct = 0.25f,
		m_radiusSpringDampPct = 0.75f,
		m_damageThresholdSpeed = 4.0f,
		m_damage = 0.1f
	};

	public float m_mass = 0.1f;

	public float m_aimStiffnessMin = 25.0f;
	public float m_aimStiffnessMax = 125.0f;
	public float m_radiusStiffnessMin = 100.0f;
	public float m_radiusStiffnessMax = 150.0f;
	public float m_springMassMax = 1.0f;

	public float m_swingDecayStiffness = 100.0f;


	public float Speed => Mathf.Abs(Mathf.Deg2Rad * (m_aimVelocityArm + m_aimVelocityItem) * ((Vector2)transform.parent.position - (Vector2)transform.position).magnitude) + Mathf.Abs(m_aimRadiusVelocity); // NOTE the conversion from angular velocity to linear speed via arclength=radians*radius // TODO: incorporate aim velocity directions?

	public bool IsSwinging => !m_aimVelocityContinuing.FloatEqual(0.0f, m_swingInfoCur.m_damageThresholdSpeed) || !m_radiusVelocityContinuing.FloatEqual(0.0f, m_swingInfoCur.m_damageThresholdSpeed);

	public bool LeftFacing => Mathf.Cos(Mathf.Deg2Rad * m_aimDegreesArm) < 0.0f; // TODO: efficiency?


	private Collider2D[] m_colliders;
	private SpriteRenderer m_renderer;
	private SpriteRenderer m_rendererParent;

	private SwingInfo m_swingInfoCur;

	private float m_massTotal;
	private float m_aimStiffness;
	private float m_radiusStiffness;

	private float m_aimDegreesArm;
	private float m_aimDegreesItem;
	private float m_aimDegreesItemRestOffsetAbs;
	private float m_aimVelocityArm;
	private float m_aimVelocityContinuing;
	private float m_aimVelocityContinuingVel;
	private float m_aimVelocityItem;
	private float m_aimRadius;
	private float m_aimRadiusVelocity;
	private float m_radiusVelocityContinuing;
	private float m_radiusVelocityContinuingVel;
	private bool m_swingDirection;


	private void Awake()
	{
		m_swingInfoCur = m_swingInfoDefault;
		m_massTotal = m_mass;
		m_aimStiffness = m_aimStiffnessMax;
		m_radiusStiffness = m_radiusStiffnessMax;

		m_colliders = GetComponents<Collider2D>();
		Collider2D parentCollider = transform.parent.GetComponent<Collider2D>();
		foreach (Collider2D collider in m_colliders)
		{
			Physics2D.IgnoreCollision(parentCollider, collider);
		}

		m_renderer = GetComponent<SpriteRenderer>();
		m_rendererParent = transform.parent.GetComponent<SpriteRenderer>();
	}

	private void LateUpdate()
	{
		// update layer/color/alpha
		// TODO: efficiency?
		gameObject.layer = transform.parent.gameObject.layer;
		m_renderer.color = m_rendererParent.color;
	}

	// TODO: combine w/ ItemController version?
	private void OnCollisionEnter2D(Collision2D collision)
	{
		KinematicObject kinematicObj = collision.gameObject.GetComponent<KinematicObject>();
		Rigidbody2D body = m_colliders.First().attachedRigidbody;
		if (kinematicObj != null && kinematicObj.ShouldIgnore(body, m_colliders, false, 0.0f, null))
		{
			return;
		}

		if (!transform.parent.GetComponent<KinematicCharacter>().CanDamage(collision.gameObject))
		{
			return;
		}

		// check speed
		float collisionSpeed = (kinematicObj == null ? collision.relativeVelocity.magnitude : (body.velocity - kinematicObj.velocity).magnitude) + Speed;
		if (collisionSpeed > m_swingInfoCur.m_damageThresholdSpeed && !m_swingInfoCur.m_damage.FloatEqual(0.0f))
		{
			Health otherHealth = collision.gameObject.GetComponent<Health>();
			if (otherHealth != null)
			{
				otherHealth.Decrement(transform.parent.gameObject, m_swingInfoCur.m_damage);
			}
		}
	}


	public /*override*/ bool ChildAttach(IAttachable attachable)
	{
		bool attached = IHolder.ChildAttachInternal(attachable, this);
		if (!attached)
		{
			return false;
		}

		if (attachable is ItemController item)
		{
			m_swingInfoCur = item.m_swingInfo;
			m_aimDegreesItemRestOffsetAbs = item.m_restDegreesOffset;
		}
		else
		{
			m_swingInfoCur = m_swingInfoDefault;
			m_aimDegreesItemRestOffsetAbs = 0.0f;
		}

		Component attachableComp = attachable.Component;
		m_massTotal = m_mass + attachableComp.GetComponents<Rigidbody2D>().Sum(body => body.mass);
		float massPctInv = Mathf.InverseLerp(m_springMassMax, m_mass, m_massTotal); // NOTE that the stiffness goes from max to min as the mass goes from min to max
		m_aimStiffness = Mathf.Lerp(m_aimStiffnessMin, m_aimStiffnessMax, massPctInv);
		m_radiusStiffness = Mathf.Lerp(m_radiusStiffnessMin, m_radiusStiffnessMax, massPctInv);

		foreach (Collider2D collider in m_colliders)
		{
			collider.enabled = false;
		}

		m_aimDegreesArm = AimDegreesRaw(transform.parent.position, Vector2.zero, attachableComp.transform.position); // TODO: lerp? use most recent rootOffset?
		m_aimVelocityArm = 0.0f;
		m_aimDegreesItem = attachableComp.transform.rotation.eulerAngles.z - m_aimDegreesArm;
		m_aimVelocityItem = 0.0f;
		m_aimRadiusVelocity = 0.0f;

		return true;
	}

	public /*override*/ void ChildDetach(IAttachable attachable, bool noAutoReplace)
	{
		m_swingInfoCur = m_swingInfoDefault; // NOTE that this has to be BEFORE ItemDetachInternal(), which might re-attach a replacement w/ a non-default SwingInfo
		m_massTotal = m_mass;
		m_aimStiffness = m_aimStiffnessMax;
		m_radiusStiffness = m_radiusStiffnessMax;

		IHolder.ChildDetachInternal(attachable, this, noAutoReplace);

		foreach (Collider2D collider in m_colliders)
		{
			collider.enabled = true;
		}
		m_aimVelocityItem = 0.0f;
	}


	public void Swing(bool isRelease)
	{
		if (isRelease)
		{
			m_aimVelocityContinuing = 0.0f;
			m_radiusVelocityContinuing = 0.0f;
			return;
		}

		AddVelocity(m_swingDirection);
		m_swingDirection = !m_swingDirection;

		// play audio if not holding anything (any held item will play audio for itself)
		if (GetComponentInChildren<IAttachable>() == null)
		{
			GetComponent<AudioSource>().PlayOneShot(GameController.Instance.m_materialSystem.Find(m_colliders.First().sharedMaterial).RandomMovementAudio()); // TODO: don't assume first collider is main material?
		}
	}

	public void UpdateAim(Vector2 rootOffset, Vector2 aimPositionArm, Vector2 aimPositionItem)
	{
		// update current speed
		m_aimVelocityArm += m_aimVelocityContinuing;
		m_aimRadiusVelocity += m_radiusVelocityContinuing;
		m_aimVelocityContinuing = DampedSpring(m_aimVelocityContinuing, 0.0f, 1.0f, false, m_swingDecayStiffness, ref m_aimVelocityContinuingVel);
		m_radiusVelocityContinuing = DampedSpring(m_radiusVelocityContinuing, 0.0f, 1.0f, false, m_swingDecayStiffness, ref m_radiusVelocityContinuingVel);

		// update current rotation
		m_aimDegreesArm = DampedSpring(m_aimDegreesArm, AimDegreesRaw(transform.parent.position, rootOffset, aimPositionArm), m_swingInfoCur.m_aimSpringDampPct, true, m_aimStiffness, ref m_aimVelocityArm);
		m_aimRadius = DampedSpring(m_aimRadius, 0.0f, m_swingInfoCur.m_radiusSpringDampPct, false, m_radiusStiffness, ref m_aimRadiusVelocity);

		// apply
		transform.localRotation = Quaternion.Euler(0.0f, 0.0f, m_aimDegreesArm);
		Vector3 localPos = (Vector3)rootOffset + (LeftFacing ? new Vector3(m_offset.x, m_offset.y, -m_offset.z) : m_offset) + transform.localRotation * Vector3.right * m_aimRadius;
		transform.localPosition = localPos;

		// update held items
		bool leftFacingCached = LeftFacing;
		foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>())
		{
			// if we're flipping the sprite, the colliders may also need to be flipped
			// TODO: don't assume vertical symmetry w/i each collider?
			if (renderer.flipY != leftFacingCached)
			{
				foreach (Collider2D collider in renderer.GetComponents<Collider2D>())
				{
					collider.offset *= new Vector2(1.0f, -1.0f);
				}
			}

			renderer.flipY = leftFacingCached;
			if (renderer.gameObject != gameObject)
			{
				m_aimDegreesItem = DampedSpring(m_aimDegreesItem, IsSwinging ? 0.0f : AimDegreesRaw(renderer.transform.position, Vector2.zero, aimPositionItem) - m_aimDegreesArm + (LeftFacing ? -m_aimDegreesItemRestOffsetAbs : m_aimDegreesItemRestOffsetAbs), m_swingInfoCur.m_aimSpringDampPct, true, m_aimStiffness, ref m_aimVelocityItem);
				renderer.transform.localRotation = Quaternion.Euler(0.0f, 0.0f, m_aimDegreesItem);
			}
		}
	}

	public void PostThrow()
	{
		AddVelocity(LeftFacing);
	}


	private float AimDegreesRaw(Vector2 rootPos, Vector2 rootOffset, Vector2 aimPosition)
	{
		Vector2 aimDiff = aimPosition - (rootPos + rootOffset + (Vector2)m_offset);
		return Mathf.Rad2Deg * Mathf.Atan2(aimDiff.y, aimDiff.x);
	}

	private void AddVelocity(bool forward)
	{
		float torqueArmLength = GetComponentsInChildren<SpriteRenderer>().Max(renderer => Mathf.Max(((Vector2)renderer.bounds.min - (Vector2)transform.position).magnitude, ((Vector2)renderer.bounds.max - (Vector2)transform.position).magnitude));

		m_aimVelocityContinuing += (forward ? m_swingInfoCur.m_angularNewtonmeters : -m_swingInfoCur.m_angularNewtonmeters) / m_massTotal / torqueArmLength;
		m_radiusVelocityContinuing += m_swingInfoCur.m_linearNewtons / m_massTotal;
	}

	private float DampedSpring(float current, float target, float dampPct, bool isAngle, float stiffness, ref float velocityCurrent)
	{
		// spring motion: F = kx - dv, where x = {vel/pos}_desired - {vel/pos}_current
		// critically damped spring: d = 2*sqrt(km)
		float dampingFactor = 2.0f * Mathf.Sqrt(m_aimStiffness * m_massTotal) * dampPct;
		UnityEngine.Assertions.Assert.IsTrue(dampingFactor > 0.0f);
		float diff = target - current;
		while (isAngle && Mathf.Abs(diff) > 180.0f)
		{
			diff -= diff < 0.0f ? -360.0f : 360.0f;
		}
		float force = stiffness * diff - dampingFactor * velocityCurrent;

		float accel = force / m_massTotal;
		velocityCurrent += accel * Time.fixedDeltaTime;

		return current + velocityCurrent * Time.fixedDeltaTime;
	}
}
