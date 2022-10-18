using System;
using System.Linq;
using UnityEngine;


/// <summary>
/// Implements game physics for some in-game, controlled entity.
/// </summary>
[DisallowMultipleComponent]
public abstract class KinematicObject : MonoBehaviour
{
	/// <summary>
	/// The priority value used to decide which of two KinematicObjects should yield when colliding; higher priorities push lower priorities
	/// </summary>
	[SerializeField] private float m_priority;

	public LayerMaskHelper m_layerIgnoreOneWay;

	/// <summary>
	/// The minimum normal (dot product) considered suitable for the entity sit on.
	/// </summary>
	public float minGroundNormalY = 0.65f;

	/// <summary>
	/// The minimum normal (dot product) considered suitable for the entity to cling to.
	/// </summary>
	public float m_minWallClingNormalY = float.MaxValue;

	/// <summary>
	/// A custom gravity coefficient applied to this entity.
	/// </summary>
	public float gravityModifier = 1f;

	/// <summary>
	/// Gravity scalar applied while wall clinging.
	/// </summary>
	public float m_wallClingGravityScalar = 0.1f;

	/// <summary>
	/// The current velocity of the entity.
	/// </summary>
	public Vector2 velocity;

	/// <summary>
	/// Is the entity currently sitting on a surface?
	/// </summary>
	/// <value></value>
	public bool IsGrounded { get; private set; }

	/// <summary>
	/// Is the entity currently clinging to a wall?
	/// </summary>
	/// <value></value>
	public bool IsWallClinging { get; private set; }


	public bool HasFlying => gravityModifier.FloatEqual(0.0f);

	public bool HasForcedVelocity => !m_velocityForcedWeight.magnitude.FloatEqual(0.0f);

	public Bounds Bounds => m_colliders.Aggregate(new Bounds() { size = Vector3.negativeInfinity }, (bounds, collider) => { bounds.Encapsulate(collider.bounds); return bounds; }); // TODO: cache local bounds?


	protected Vector2 targetVelocity;
	protected Vector2 groundNormal = Vector2.up;
	protected Vector2 m_wallNormal;
	protected Rigidbody2D body;
	protected ContactFilter2D contactFilter;
	protected RaycastHit2D[] hitBuffer = new RaycastHit2D[16];

	protected const float minMoveDistance = 0.001f;
	private const float m_shellRadius = Utility.FloatEpsilon;


	private KinematicCharacter m_character;
	protected Collider2D[] m_colliders;
	private AvatarController m_avatar;

	private Vector2 m_velocityForced = Vector2.zero;
	private Vector2 m_velocityForcedWeight = Vector2.zero;
	private Vector2 m_velocityForcedWeightVel = Vector2.zero;
	private Vector2 m_velocityForcedSmoothTimes = Vector2.one;

	private int m_layerIdxOrig;
	private const float m_wallPushDisableSeconds = 0.25f;

	private const float m_nearGroundDistance = 1.0f;


	/// <summary>
	/// Bounce the objects velocity in a direction.
	/// </summary>
	/// <param name="dir"></param>
	public void Bounce(Vector2 dir, float decayTimeX = 0.25f, float decayTimeY = 0.1f)
	{
		m_velocityForced = dir;
		m_velocityForcedWeight = Vector2.one;
		m_velocityForcedWeightVel = Vector2.zero;
		m_velocityForcedSmoothTimes = new(decayTimeX, decayTimeY);
	}

	/// <summary>
	/// Teleport to some position.
	/// </summary>
	/// <param name="position"></param>
	public void Teleport(Vector3 position)
	{
		transform.position = position;
		if (body != null)
		{
			body.position = position;
			body.velocity *= 0;
		}
		velocity *= 0;
	}

	protected virtual void Awake()
	{
		body = GetComponent<Rigidbody2D>();
		body.isKinematic = true;
		m_character = GetComponent<KinematicCharacter>();
		m_colliders = GetComponents<Collider2D>(); // NOTE that we purposely ignore colliders on child objects
		m_avatar = GetComponent<AvatarController>();
		m_layerIdxOrig = gameObject.layer;
	}

	protected virtual void Start()
	{
		contactFilter.useTriggers = false;
	}

	protected virtual void Update()
	{
		targetVelocity = Vector2.zero;
		ComputeVelocity();
	}

	protected virtual void ComputeVelocity()
	{
	}

	protected virtual void FixedUpdate()
	{
		if (!body.simulated)
		{
			return;
		}

		// if partially overlapping other geometry, separate
		// TODO: efficiency?
		Vector2 totalOverlap = Vector2.zero;
		foreach (Collider2D collider in m_colliders)
		{
			System.Collections.Generic.List<ContactPoint2D> contacts = new();
			collider.GetContacts(contacts);
			foreach (ContactPoint2D contact in contacts)
			{
				if (collider.ShouldIgnore(contact.rigidbody, new[] { contact.collider }, body.mass, typeof(AnchoredJoint2D), 0.1f))
				{
					continue;
				}

				// resolve kinematic-kinematic collisions
				KinematicObject otherKinematic = contact.collider.GetComponent<KinematicObject>();
				if (otherKinematic != null && otherKinematic.m_priority < m_priority)
				{
					continue;
				}

				Vector2 newOverlap = contact.normal * -contact.separation;
				if (newOverlap.x.FloatEqual(0.0f) && newOverlap.y.FloatEqual(0.0f))
				{
					continue;
				}

				// TODO: reconcile overlaps in opposing directions?
				totalOverlap.x = Mathf.Abs(newOverlap.x) > Mathf.Abs(totalOverlap.x) ? newOverlap.x : totalOverlap.x;
				totalOverlap.y = Mathf.Abs(newOverlap.y) > Mathf.Abs(totalOverlap.y) ? newOverlap.y : totalOverlap.y;
			}
		}
		transform.position += (Vector3)totalOverlap;

		//if already falling, fall faster than the jump speed, otherwise use normal gravity.
		velocity += (IsWallClinging ? m_wallClingGravityScalar : 1.0f) * (velocity.y < 0.0f ? gravityModifier : 1.0f) * Time.fixedDeltaTime * Physics2D.gravity;

		velocity.x = Mathf.Lerp(targetVelocity.x, m_velocityForced.x, m_velocityForcedWeight.x);
		if (!m_velocityForcedWeight.y.FloatEqual(0.0f))
		{
			velocity.y = Mathf.Lerp(velocity.y, m_velocityForced.y, m_velocityForcedWeight.y);
		}
		else if (HasFlying)
		{
			velocity.y = targetVelocity.y;
		}

		// blend velocity back from forced if necessary
		m_velocityForcedWeight = m_velocityForcedWeight.SmoothDamp(Vector2.zero, ref m_velocityForcedWeightVel, m_velocityForcedSmoothTimes);

		IsGrounded = false;
		IsWallClinging = false;

		Vector2 deltaPosition = velocity * Time.fixedDeltaTime;

		Vector2 moveAlongGround = new(groundNormal.y, -groundNormal.x);

		Vector2 move = moveAlongGround * deltaPosition.x;

		// update our collision mask
		bool shouldIgnoreOneWays = velocity.y >= 0.0f || (m_character != null && m_character.IsDropping); // TODO: don't require knowledge of KinematicCharacter
		gameObject.layer = shouldIgnoreOneWays ? m_layerIgnoreOneWay.ToIndex() : m_layerIdxOrig;
		contactFilter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));

		Lazy<bool> isNearGround = new(() => Physics2D.Raycast(Bounds.min, Vector2.down, m_nearGroundDistance, GameController.Instance.m_layerWalls).collider != null, false); // TODO: cheaper way to avoid starting wall cling when right above the ground? cast whole collider for better detection?
		PerformMovement(move, false, ref isNearGround);

		move = Vector2.up * deltaPosition.y;

		PerformMovement(move, true, ref isNearGround);
	}

	protected virtual void DespawnSelf()
	{
		Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
	}


	void PerformMovement(Vector2 move, bool yMovement, ref Lazy<bool> isNearGround)
	{
		float distance = move.magnitude;

		if (!yMovement && distance < minMoveDistance) // NOTE that even if we aren't moving vertically, we may still need to push out of the ground
		{
			return;
		}

		// check if we hit anything in current direction of travel
		foreach (Collider2D collider in m_colliders)
		{
			int count = collider.Cast(move, contactFilter, hitBuffer, distance + m_shellRadius, true); // NOTE that we ignore child colliders such as arms
			for (int i = 0; i < count; i++)
			{
				RaycastHit2D hit = hitBuffer[i];
				KinematicObject otherKinematic = hit.collider.GetComponent<KinematicObject>();
				if (collider.ShouldIgnore(hit.rigidbody, new[] { hit.collider }, body.mass, typeof(AnchoredJoint2D), 0.1f) || (otherKinematic != null && otherKinematic.m_priority < m_priority))
				{
					// push-through floor/walls prevention
					if (hit.transform.parent == null && hit.rigidbody != null && hit.rigidbody.IsTouchingLayers(GameController.Instance.m_layerWalls))
					{
						Hazard hazard = hit.collider.GetComponent<Hazard>();
						if (hazard == null || !hazard.enabled) // TODO: more general way of ensuring "important" collisions aren't ignored?
						{
							EnableCollision.TemporarilyDisableCollision(m_colliders, new[] { hit.collider }, m_wallPushDisableSeconds);
						}
					}

					continue; // don't get hung up on dynamic/carried/ignored objects
				}

				Vector2 currentNormal = hit.normal;

				// is this surface flat enough to land on?
				if (currentNormal.y >= minGroundNormalY)
				{
					IsGrounded = true;
					IsWallClinging = false;
					// if moving down, change the groundNormal to new surface normal.
					if (move.y < 0.0f)
					{
						groundNormal = currentNormal;
						currentNormal.x = 0;
					}
				}
				if (!IsGrounded && currentNormal.y >= m_minWallClingNormalY && velocity.y <= 0.0f && !isNearGround.Value)
				{
					IsWallClinging = true;
					m_wallNormal = currentNormal;
				}
				if (!HasFlying)
				{
					if (IsGrounded)
					{
						// how much of our velocity aligns with surface normal?
						float projection = Vector2.Dot(velocity, currentNormal);
						if (projection < 0)
						{
							// slower velocity if moving against the normal (up a hill).
							velocity -= projection * currentNormal;
						}
					}
					else if (!isNearGround.Value)
					{
						// airborne, but hit something, so cancel horizontal velocity.
						velocity.x = 0.0f;
					}
				}
				// remove shellDistance from actual move distance.
				float modifiedDistance = hit.distance - m_shellRadius; // TODO: don't move backward when colliding from multiple directions in-place
				distance = modifiedDistance < distance ? modifiedDistance : distance;
			}
		}
		body.position += move.normalized * distance;

		// detect out-of-bounds experiences
		if (body.position.y < -1000.0f) // TODO: automatically determine suitable lower bound? remove / expand as necessary for runtime generation?
		{
			if (m_avatar != null)
			{
				m_avatar.Teleport(Vector3.zero); // TODO: better handling?
			}
			else
			{
				DespawnSelf(); // TODO: ensure important objects such as keys aren't despawned?
			}
		}
	}
}
