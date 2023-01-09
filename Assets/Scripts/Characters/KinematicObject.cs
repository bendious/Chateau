using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


/// <summary>
/// Implements game physics for some in-game, controlled entity.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody2D))]
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

	public bool HasForcedVelocity => m_forcedVelocities.Count > 0;

	public Bounds Bounds => (m_colliders != null && m_colliders.Length > 0 ? m_colliders : GetComponents<Collider2D>()).ToBounds(); // NOTE the extra logic to support being used before Awake() to support prefabs // TODO: cache local bounds?


	public Vector2 TargetVelocity { get; protected set; }
	private Vector2 m_groundNormal = Vector2.up;
	private PhysicsMaterial2D m_groundMaterial;
	protected Vector2 m_wallNormal;
	protected Rigidbody2D body;
	private ContactFilter2D m_contactFilter;
	private readonly List<RaycastHit2D> m_hitBuffer = new();

	protected const float minMoveDistance = 0.001f;
	private const float m_shellRadius = Utility.FloatEpsilon;


	private KinematicCharacter m_character;
	protected Collider2D[] m_colliders;
	private AvatarController m_avatar;

	private class ForcedVelocity
	{
		public readonly Vector2 m_velocityOrig;
		public readonly Vector2 m_decayTimes;
		public Vector2 m_weight = Vector2.one;
		public Vector2 m_weightVel = Vector2.zero;

		public ForcedVelocity(Vector2 velocity, Vector2 decayTimes) { m_velocityOrig = velocity; m_decayTimes = decayTimes; }
	}
	private readonly List<ForcedVelocity> m_forcedVelocities = new();

	private int m_layerIdxOrig;
	private const float m_wallPushDisableSeconds = 0.25f;

	private const float m_nearGroundDistance = 1.0f;


	/// <summary>
	/// Bounce the objects velocity in a direction.
	/// </summary>
	/// <param name="velocity"></param>
	public void Bounce(Vector2 velocity, float decayTimeX = 0.25f, float decayTimeY = 0.1f) => m_forcedVelocities.Add(new ForcedVelocity(velocity, new Vector2(decayTimeX, decayTimeY)));

	public void BounceCancel() => m_forcedVelocities.Clear();

	/// <summary>
	/// Teleport to some position.
	/// </summary>
	/// <param name="position"></param>
	public virtual void Teleport(Vector3 position)
	{
		transform.position = position;
		if (body != null)
		{
			body.position = position;
			body.velocity *= 0;
		}
		velocity *= 0;
		BounceCancel();
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
		m_contactFilter.useTriggers = false;
	}

	protected virtual void Update()
	{
		TargetVelocity = Vector2.zero;
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
			List<ContactPoint2D> contacts = new();
			collider.GetContacts(contacts);
			foreach (ContactPoint2D contact in contacts)
			{
				if (collider.ShouldIgnore(contact.rigidbody, new[] { contact.collider }, body.mass, typeof(AnchoredJoint2D), 0.1f))
				{
					continue;
				}

				// resolve kinematic-kinematic collisions
				KinematicObject otherKinematic = contact.collider.GetComponent<KinematicObject>();
				if (otherKinematic != null)
				{
					KinematicCollision evt = Simulation.Schedule<KinematicCollision>();
					evt.m_component1 = this;
					evt.m_component2 = otherKinematic;
					evt.m_position = contact.point;
					evt.m_normal = contact.normal;

					if (otherKinematic.m_priority < m_priority)
					{
						continue;
					}
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
		transform.position += (Vector3)totalOverlap; // TODO: ensure new position is w/i a room?

		//if already falling, fall faster than the jump speed, otherwise use normal gravity.
		velocity += (IsWallClinging ? m_wallClingGravityScalar : 1.0f) * (velocity.y < 0.0f ? gravityModifier : 1.0f) * Time.fixedDeltaTime * Physics2D.gravity;

		// apply target/forced velocities
		Tuple<Vector2, Vector2> forcedVelSum = m_forcedVelocities.Aggregate(Tuple.Create(Vector2.zero, Vector2.zero), (sum, fv) => Tuple.Create(sum.Item1 + fv.m_velocityOrig * fv.m_weight, sum.Item2 + fv.m_weight));
		velocity.x = Mathf.Lerp(TargetVelocity.x, forcedVelSum.Item1.x, forcedVelSum.Item2.x); // NOTE that this caps the summed weights at 1.0
		if (!forcedVelSum.Item2.y.FloatEqual(0.0f))
		{
			velocity.y = Mathf.Lerp(velocity.y, forcedVelSum.Item1.y, forcedVelSum.Item2.y); // NOTE that this caps the summed weights at 1.0
		}
		else if (HasFlying)
		{
			velocity.y = TargetVelocity.y;
		}

		// blend out / remove forced velocities
		List<ForcedVelocity> fvToRemove = new(m_forcedVelocities.Count); // TODO: don't re-allocate each frame?
		foreach (ForcedVelocity fv in m_forcedVelocities)
		{
			fv.m_weight = fv.m_weight.SmoothDamp(Vector2.zero, ref fv.m_weightVel, fv.m_decayTimes);
			if (fv.m_weight.x.FloatEqual(0.0f) && fv.m_weight.y.FloatEqual(0.0f))
			{
				fvToRemove.Add(fv);
			}
		}
		if (fvToRemove.Count > 0)
		{
			int nRemoved = m_forcedVelocities.RemoveAll(fv => fvToRemove.Contains(fv));
			if (nRemoved != fvToRemove.Count)
			{
				Debug.LogWarning("Unremoved forced velocity?");
			}
		}

		IsGrounded = false;
		IsWallClinging = false;

		Vector2 deltaPosition = velocity * Time.fixedDeltaTime;

		Vector2 moveAlongGround = new(m_groundNormal.y, -m_groundNormal.x);

		Vector2 move = moveAlongGround * deltaPosition.x;

		// update our collision mask
		bool shouldIgnoreOneWays = velocity.y >= 0.0f || (m_character != null && m_character.IsDropping); // TODO: don't require knowledge of KinematicCharacter
		gameObject.layer = shouldIgnoreOneWays ? m_layerIgnoreOneWay.ToIndex() : m_layerIdxOrig;
		m_contactFilter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));

		Lazy<bool> isNearGround = new(() => Physics2D.Raycast(Bounds.min, Vector2.down, m_nearGroundDistance, GameController.Instance.m_layerWalls).collider != null, false); // TODO: cheaper way to avoid starting wall cling when right above the ground? cast whole collider for better detection?
		PerformMovement(move, false, ref isNearGround);

		move = Vector2.up * deltaPosition.y;

		PerformMovement(move, true, ref isNearGround);
	}


	protected virtual void DespawnSelf()
	{
		Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "called by animation triggers")]
	private void GroundAudioEvent(AnimationEvent evt)
	{
		PhysicsMaterial2D colliderMaterial = m_colliders.Select(c => c.sharedMaterial).FirstOrDefault(m => m != null); // TODO: prioritize between multiple different materials?
		MaterialPairInfo info = GameController.Instance.m_materialSystem.PairBestMatch(m_groundMaterial, colliderMaterial != null ? colliderMaterial : body.sharedMaterial);
		AudioSource source = GetComponent<AudioSource>(); // TODO: cache?
		source.PlayOneShot(evt.intParameter == 0 || info.m_collisionStrongAudio.Length <= 0 ? info.m_collisionAudio.Random() : info.m_collisionStrongAudio.RandomWeighted()); // TODO: separate arrays for collision/ground SFX?
	}


	private void PerformMovement(Vector2 move, bool yMovement, ref Lazy<bool> isNearGround)
	{
		float distance = move.magnitude;

		if (!yMovement && distance < minMoveDistance) // NOTE that even if we aren't moving vertically, we may still need to push out of the ground
		{
			return;
		}

		// check if we hit anything in current direction of travel
		foreach (Collider2D collider in m_colliders)
		{
			m_hitBuffer.Clear();
			int count = collider.Cast(move, m_contactFilter, m_hitBuffer, distance + m_shellRadius, true); // NOTE that we ignore child colliders such as arms
			for (int i = 0; i < count; i++)
			{
				RaycastHit2D hit = m_hitBuffer[i];
				KinematicObject otherKinematic = hit.collider.GetComponent<KinematicObject>();
				if (otherKinematic != null)
				{
					KinematicCollision evt = Simulation.Schedule<KinematicCollision>();
					evt.m_component1 = this;
					evt.m_component2 = otherKinematic;
					evt.m_position = hit.point;
					evt.m_normal = hit.normal;
				}
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
						m_groundNormal = currentNormal;
						m_groundMaterial = hit.collider.sharedMaterial != null ? hit.collider.sharedMaterial : hit.rigidbody.sharedMaterial;
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
