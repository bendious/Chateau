using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;


/// <summary>
/// Implements game physics for some in-game, controlled entity.
/// </summary>
public abstract class KinematicObject : MonoBehaviour
{
	// TODO: give appropriate defaults w/o needing LayerMask.NameToLayer() at initialization time?
	public LayerMask m_platformLayer;
	public LayerMask m_ignorePlatformsLayer;

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

	public float m_velocityForcedSmoothTime = 0.25f;

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


	protected Vector2 targetVelocity;
	protected Vector2 groundNormal = Vector2.up;
	protected Vector2 m_wallNormal;
	protected Rigidbody2D body;
	protected ContactFilter2D contactFilter;
	protected RaycastHit2D[] hitBuffer = new RaycastHit2D[16];

	protected const float minMoveDistance = 0.001f;
	private const float shellRadius = 0.01f;


	private int m_spawnCheckFrameCount = 2; // NOTE that since we use Collider2D.GetContacts(), we can't just check immediately in Start() or FixedUpdate() before physics first runs...

	private KinematicCharacter m_character;
	protected Collider2D m_collider; // TODO: handle multi-collider objects?
	private AvatarController m_avatar;

	private Vector2 m_velocityForced = Vector2.zero;
	private Vector2 m_velocityForcedWeight = Vector2.zero;
	private Vector2 m_velocityForcedWeightVel = Vector2.zero;

	private int m_layerOrig;
	private int m_platformLayerIdx;
	private int m_ignorePlatformsLayerIdx;
	private const float m_platformTopEpsilon = 0.1f;

	private const float m_nearGroundDistance = 1.0f;


	/// <summary>
	/// Bounce the objects velocity in a direction.
	/// </summary>
	/// <param name="dir"></param>
	public void Bounce(Vector2 dir)
	{
		m_velocityForced = dir;
		m_velocityForcedWeight = Vector2.one;
		m_velocityForcedWeightVel = Vector2.zero;
	}

	/// <summary>
	/// Teleport to some position.
	/// </summary>
	/// <param name="position"></param>
	public void Teleport(Vector3 position)
	{
		body.position = position;
		velocity *= 0;
		body.velocity *= 0;
	}

	protected virtual void Awake()
	{
		body = GetComponent<Rigidbody2D>();
		body.isKinematic = true;
		m_character = GetComponent<KinematicCharacter>();
		m_collider = GetComponent<Collider2D>();
		m_avatar = GetComponent<AvatarController>();
		m_layerOrig = gameObject.layer;
	}

	protected virtual void Start()
	{
		contactFilter.useTriggers = false;

		// TODO: detect updates while active?
		Assert.AreNotEqual(m_platformLayer, -1);
		Assert.AreNotEqual(m_ignorePlatformsLayer, -1);
		m_platformLayerIdx = Mathf.RoundToInt(Mathf.Log(m_platformLayer.value, 2.0f));
		m_ignorePlatformsLayerIdx = Mathf.RoundToInt(Mathf.Log(m_ignorePlatformsLayer.value, 2.0f));
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

		if (m_spawnCheckFrameCount > 0)
		{
			// if spawned partially overlapping other geometry, separate
			System.Collections.Generic.List<ContactPoint2D> contacts = new();
			m_collider.GetContacts(contacts);

			Vector2 totalOverlap = Vector2.zero;
			foreach (ContactPoint2D contact in contacts)
			{
				if (ShouldIgnore(contact.rigidbody, new Collider2D[] { contact.collider }, false, body.mass, typeof(AnchoredJoint2D)))
				{
					continue;
				}
				Vector2 newOverlap = contact.normal * -contact.separation;
				totalOverlap.x = Mathf.Abs(newOverlap.x) > Mathf.Abs(totalOverlap.x) ? newOverlap.x : totalOverlap.x;
				totalOverlap.y = Mathf.Abs(newOverlap.y) > Mathf.Abs(totalOverlap.y) ? newOverlap.y : totalOverlap.y;
			}
			transform.position += (Vector3)totalOverlap;

			--m_spawnCheckFrameCount;
		}

		//if already falling, fall faster than the jump speed, otherwise use normal gravity.
		velocity += (IsWallClinging ? m_wallClingGravityScalar : 1.0f) * (velocity.y < 0.0f ? gravityModifier : 1.0f) * Time.fixedDeltaTime * Physics2D.gravity;

		velocity.x = Mathf.Lerp(targetVelocity.x, m_velocityForced.x, m_velocityForcedWeight.x);
		if (HasFlying && m_velocityForcedWeight.y.FloatEqual(0.0f))
		{
			velocity.y = targetVelocity.y;
		}

		// blend velocity back from forced if necessary
		m_velocityForcedWeight = m_velocityForcedWeight.SmoothDamp(Vector2.zero, ref m_velocityForcedWeightVel, m_velocityForcedSmoothTime);

		IsGrounded = false;
		IsWallClinging = false;

		Vector2 deltaPosition = velocity * Time.fixedDeltaTime;

		Vector2 moveAlongGround = new(groundNormal.y, -groundNormal.x);

		Vector2 move = moveAlongGround * deltaPosition.x;

		// update our collision mask
		bool shouldIgnoreOneWays = velocity.y >= 0.0f || (m_character != null && m_character.IsDropping); // TODO: don't require knowledge of KinematicCharacter
		gameObject.layer = shouldIgnoreOneWays ? m_ignorePlatformsLayerIdx : m_layerOrig;
		contactFilter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));

		Lazy<bool> isNearGround = new(() => Physics2D.Raycast(m_collider.bounds.min, Vector2.down, m_nearGroundDistance).collider != null, false); // TODO: cheaper way to avoid starting wall cling when right above the ground? cast whole collider for better detection?
		PerformMovement(move, false, ref isNearGround);

		move = Vector2.up * deltaPosition.y;

		PerformMovement(move, true, ref isNearGround);
	}

	public bool ShouldIgnore(Rigidbody2D body, Collider2D[] colliders, bool ignoreStatics, float dynamicsMassThreshold, Type ignoreChildrenExcept)
	{
		Assert.IsTrue(colliders != null && colliders.Length > 0);
		GameObject otherObj = colliders.First().gameObject; // NOTE that we don't use the rigid body's object since that can be separate from the collider object (e.g. characters and arms) // TODO: ensure all colliders are from the same object & body?
		if (otherObj == gameObject)
		{
			return true; // ignore our own object
		}
		if (ignoreStatics && (body == null || body.bodyType == RigidbodyType2D.Static))
		{
			return true;
		}
		if (body != null && body.bodyType == RigidbodyType2D.Dynamic && body.mass < dynamicsMassThreshold)
		{
			return true;
		}
		if (ignoreChildrenExcept != null && body != null && (body.transform.parent != null || body.gameObject != otherObj) && body.GetComponent(ignoreChildrenExcept) == null)
		{
			return true; // ignore non-root bodies (e.g. arms)
		}
		for (Transform transformItr = otherObj.transform; transformItr != null; transformItr = transformItr.parent)
		{
			if (transformItr == transform)
			{
				return true; // ignore child objects
			}
		}

		// ignore objects flagged to ignore each other and their children
		// TODO: efficiency?
		foreach (Collider2D collider in colliders)
		{
			if (Physics2D.GetIgnoreCollision(m_collider, collider))
			{
				return true;
			}
			if (transform.parent != null)
			{
				Collider2D parentCollider = transform.parent.GetComponent<Collider2D>();
				if (parentCollider != null && Physics2D.GetIgnoreCollision(parentCollider, collider))
				{
					return true;
				}
			}
		}
		if (otherObj.transform.parent != null)
		{
			Collider2D parentCollider = otherObj.transform.parent.GetComponent<Collider2D>();
			if (parentCollider != null && Physics2D.GetIgnoreCollision(m_collider, parentCollider))
			{
				return true;
			}
		}

		return false;
	}

	protected virtual void DespawnSelf()
	{
		Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
	}


	void PerformMovement(Vector2 move, bool yMovement, ref Lazy<bool> isNearGround)
	{
		float distance = move.magnitude;

		if (yMovement || distance >= minMoveDistance) // NOTE that even if we aren't moving vertically, we may still need to push out of the ground
		{
			//check if we hit anything in current direction of travel
			int count = m_collider.Cast(move, contactFilter, hitBuffer, distance + shellRadius, true); // NOTE that we ignore child colliders such as arms
			for (int i = 0; i < count; i++)
			{
				RaycastHit2D hit = hitBuffer[i];
				if (ShouldIgnore(hit.rigidbody, new Collider2D[] { hit.collider }, false, body.mass, typeof(AnchoredJoint2D)))
				{
					continue; // don't get hung up on dynamic/carried/ignored objects
				}
				if (hit.collider.gameObject.layer == m_platformLayerIdx && m_collider.bounds.min.y + m_platformTopEpsilon < hit.collider.bounds.max.y)
				{
					continue; // if partway through a one-way platform, ignore it
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
						//how much of our velocity aligns with surface normal?
						float projection = Vector2.Dot(velocity, currentNormal);
						if (projection < 0)
						{
							//slower velocity if moving against the normal (up a hill).
							velocity -= projection * currentNormal;
						}
					}
					else if (!isNearGround.Value)
					{
						// airborne, but hit something, so cancel horizontal velocity.
						velocity.x = 0.0f;
					}
				}
				//remove shellDistance from actual move distance.
				float modifiedDistance = hit.distance - shellRadius;
				distance = modifiedDistance < distance ? modifiedDistance : distance;
			}
		}
		body.position += move.normalized * distance;

		// detect out-of-bounds experiences
		if (body.position.y < -1000.0f) // TODO: automatically determine suitable lower bound?
		{
			if (m_avatar != null)
			{
				m_avatar.OnDeath();
			}
			else
			{
				DespawnSelf();
			}
		}
	}
}
