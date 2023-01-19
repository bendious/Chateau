using System.Linq;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public sealed class ArmController : MonoBehaviour, IHolder
{
	public /*override*/ int HoldCountMax => 1;

	[SerializeField] private Vector3 m_offset;
	public /*override*/ Vector3 AttachOffsetLocal => m_offset;
	public /*override*/ Vector3 ChildAttachPointLocal => Vector3.right * GetComponent<SpriteRenderer>().sprite.bounds.size.x;

	[SerializeField] private SwingInfo m_swingInfoDefault = new() {
		m_angularNewtonmeters = 300.0f,
		m_linearNewtons = 0.1f,
		m_aimSpringDampPct = 0.25f,
		m_radiusSpringDampPct = 0.75f,
		m_damageThresholdSpeed = 4.0f,
		m_damage = 0.1f
	};
	[SerializeField] private SwingInfo m_jabInfo = new() {
		m_angularNewtonmeters = 25.0f,
		m_linearNewtons = 25.0f,
		m_aimSpringDampPct = 0.25f,
		m_radiusSpringDampPct = 0.75f,
		m_damageThresholdSpeed = 4.0f,
		m_damage = 0.1f
	};

	[SerializeField] private float m_mass = 0.1f;

	[SerializeField] private float m_radiusBase = 0.0f;

	[SerializeField] private float m_aimStiffnessMin = 25.0f;
	[SerializeField] private float m_aimStiffnessMax = 100.0f;
	[SerializeField] private float m_radiusStiffnessMin = 25.0f;
	[SerializeField] private float m_radiusStiffnessMax = 100.0f;
	[SerializeField] private float m_springMassMax = 1.0f;

	[SerializeField] private float m_swingDecayStiffness = 100.0f;

	[SerializeField] private bool m_noRotation = false;

	public bool m_colorMatching = true;


	public float Velocity => Mathf.Deg2Rad * (m_aimVelocityArm + m_aimVelocityItem) * ((Vector2)transform.parent.position - (Vector2)transform.position).magnitude + m_aimRadiusVelocity; // NOTE the conversion from angular velocity to linear speed via arclength=radians*radius
	public float Speed => Mathf.Abs(Velocity);

	public bool IsSwinging => !m_aimVelocityContinuing.FloatEqual(0.0f, m_swingInfoCur.m_damageThresholdSpeed) || !m_radiusVelocityContinuing.FloatEqual(0.0f, m_swingInfoCur.m_damageThresholdSpeed);

	public bool LeftFacing => m_character.LeftFacing;


	private Collider2D[] m_colliders;
	private SpriteRenderer m_renderer;
	private SpriteRenderer m_rendererParent;
	private KinematicCharacter m_character;

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
		m_character = transform.parent.GetComponent<KinematicCharacter>();
	}

	private void LateUpdate()
	{
		// update layer/color/alpha
		// TODO: efficiency?
		gameObject.layer = transform.parent.gameObject.layer;
		if (m_colorMatching)
		{
			m_renderer.color = m_rendererParent.color;
		}
		else
		{
			Color color = m_renderer.color;
			color.a = m_rendererParent.color.a;
			m_renderer.color = color;
		}
	}

	// TODO: combine w/ ItemController version?
	private void OnCollisionEnter2D(Collision2D collision)
	{
		Rigidbody2D body = m_colliders.First().attachedRigidbody;
		if (collision.collider.ShouldIgnore(body, m_colliders, oneWayTopEpsilon: float.MaxValue))
		{
			return;
		}

		if (!m_character.CanDamage(collision.gameObject))
		{
			return;
		}

		// check speed
		KinematicObject kinematicObj = collision.gameObject.GetComponent<KinematicObject>();
		float collisionSpeed = (kinematicObj == null ? collision.relativeVelocity.magnitude : (body.velocity - kinematicObj.velocity).magnitude) + Speed;
		if (collisionSpeed > m_swingInfoCur.m_damageThresholdSpeed && !m_swingInfoCur.m_damage.FloatEqual(0.0f))
		{
			if (collision.gameObject.TryGetComponent(out Health otherHealth))
			{
				otherHealth.Decrement(transform.parent.gameObject, gameObject, m_swingInfoCur.m_damage, m_swingInfoCur.m_damageType);
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

		m_aimDegreesArm = AimDegreesRaw(transform.parent.position, Vector2.zero, attachableComp.transform.position, m_aimDegreesArm); // TODO: lerp? use most recent rootOffset?
		m_aimVelocityArm = 0.0f;
		m_aimVelocityContinuing = 0.0f;
		m_aimVelocityContinuingVel = 0.0f;
		m_aimRadiusVelocity = 0.0f;
		m_radiusVelocityContinuing = 0.0f;
		m_radiusVelocityContinuingVel = 0.0f;
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


	public void Swing(bool isRelease, bool isJab)
	{
		if (isRelease)
		{
			m_aimVelocityContinuing = 0.0f;
			m_radiusVelocityContinuing = 0.0f;
			return;
		}

		AddVelocity(m_swingDirection, isJab);

		// play audio if not holding anything (any held item will play audio for itself)
		if (GetComponentInChildren<IAttachable>() == null)
		{
			GetComponent<AudioSource>().PlayOneShot(GameController.Instance.m_materialSystem.Find(m_colliders.First().sharedMaterial).m_movementAudio.Random()); // TODO: don't assume first collider is main material?
		}
	}

	public void UpdateAim(Vector2 rootOffset, Vector2 aimPositionArm, Vector2 aimPositionItem)
	{
		// update current speed
		m_aimVelocityArm += m_aimVelocityContinuing;
		m_aimRadiusVelocity += m_radiusVelocityContinuing;
		m_aimVelocityContinuing = Utility.DampedSpring(m_aimVelocityContinuing, 0.0f, 1.0f, false, m_swingDecayStiffness, m_massTotal, ref m_aimVelocityContinuingVel);
		m_radiusVelocityContinuing = Utility.DampedSpring(m_radiusVelocityContinuing, 0.0f, 1.0f, false, m_swingDecayStiffness, m_massTotal, ref m_radiusVelocityContinuingVel);

		// update current rotation
		float targetDegreesArm = AimDegreesRaw(transform.parent.position, rootOffset, aimPositionArm, m_aimDegreesArm);
		m_aimDegreesArm = Utility.DampedSpring(m_aimDegreesArm, targetDegreesArm, m_swingInfoCur.m_aimSpringDampPct, true, m_aimStiffness, m_massTotal, ref m_aimVelocityArm);
		m_aimRadius = Utility.DampedSpring(m_aimRadius, m_radiusBase, m_swingInfoCur.m_radiusSpringDampPct, false, m_radiusStiffness, m_massTotal, ref m_aimRadiusVelocity);
		m_swingDirection = (targetDegreesArm - m_aimDegreesArm).Modulo(360.0f) < 180.0f; // should swing "up" if we are "below" the current target angle

		// apply
		Quaternion rotationArm = Quaternion.Euler(0.0f, 0.0f, m_aimDegreesArm);
		if (!m_noRotation)
		{
			transform.localRotation = rotationArm;
		}
		transform.localPosition = (Vector3)rootOffset + (LeftFacing ? new(m_offset.x, m_offset.y, -m_offset.z) : m_offset) + rotationArm * Vector3.right * m_aimRadius;

		// maybe flip sprite
		void maybeFlipSprite(SpriteRenderer renderer)
		{
			// if we're flipping the sprite, the colliders may also need to be flipped
			// TODO: don't assume vertical symmetry w/i each collider?
			if (renderer.flipY != LeftFacing)
			{
				foreach (Collider2D collider in renderer.GetComponents<Collider2D>())
				{
					collider.offset *= new Vector2(1.0f, -1.0f);
				}
			}

			renderer.flipY = LeftFacing;
		}
		maybeFlipSprite(m_renderer);

		// update held items
		bool hasJoint = GetComponentInChildren<Joint2D>() != null;
		for (int i = 0; i < transform.childCount; ++i)
		{
			Transform childTf = transform.GetChild(i);
			m_aimDegreesItem = Utility.DampedSpring(m_aimDegreesItem, IsSwinging ? 0.0f : AimDegreesRaw(childTf.position, Vector2.zero, aimPositionItem, m_aimDegreesItem) - m_aimDegreesArm + (LeftFacing ? -m_aimDegreesItemRestOffsetAbs : m_aimDegreesItemRestOffsetAbs), 1.0f, true, m_aimStiffness, m_massTotal, ref m_aimVelocityItem); // NOTE that aiming for all items uses critical damping rather than m_swingInfoCur.m_aimSpringDampPct, to prevent overly-annoying aim jiggle // TODO: parameterize?
			childTf.localRotation = Quaternion.Euler(0.0f, 0.0f, m_aimDegreesItem);

			if (hasJoint)
			{
				continue; // still-connected joint objects (e.g. lightbulbs) may rely on the sprite being un-flipped
			}
			maybeFlipSprite(childTf.GetComponentInChildren<SpriteRenderer>());
		}
	}

	public void PostThrow()
	{
		AddVelocity(LeftFacing, false);
	}


	private float AimDegreesRaw(Vector2 rootPos, Vector2 rootOffset, Vector2 aimPosition, float degreesPrev)
	{
		Vector2 aimDiff = aimPosition - (rootPos + rootOffset + (Vector2)m_offset);
		return aimDiff.x.FloatEqual(0.0f) && aimDiff.y.FloatEqual(0.0f) ? degreesPrev : Utility.ZDegrees(aimDiff);
	}

	private void AddVelocity(bool forward, bool isJab)
	{
		float torqueArmLength = GetComponentsInChildren<SpriteRenderer>().Max(renderer => Mathf.Max(((Vector2)renderer.bounds.min - (Vector2)transform.position).magnitude, ((Vector2)renderer.bounds.max - (Vector2)transform.position).magnitude));

		SwingInfo infoFinal = isJab ? m_jabInfo : m_swingInfoCur; // TODO: handle "jabbing" with a held item via per-item jab speeds?
		m_aimVelocityContinuing += (forward ? infoFinal.m_angularNewtonmeters : -infoFinal.m_angularNewtonmeters) / m_massTotal / torqueArmLength;
		m_radiusVelocityContinuing += infoFinal.m_linearNewtons / m_massTotal;
	}
}
