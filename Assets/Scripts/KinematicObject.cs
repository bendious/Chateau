using System;
using UnityEngine;
using UnityEngine.Assertions;


namespace Platformer.Mechanics
{
	/// <summary>
	/// Implements game physics for some in game entity.
	/// </summary>
	public class KinematicObject : MonoBehaviour
	{
		/// <summary>
		/// The minimum normal (dot product) considered suitable for the entity sit on.
		/// </summary>
		public float minGroundNormalY = .65f;

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


		protected Vector2 targetVelocity;
		protected Vector2 groundNormal;
		protected Vector2 m_wallNormal;
		protected Rigidbody2D body;
		protected ContactFilter2D contactFilter;
		protected RaycastHit2D[] hitBuffer = new RaycastHit2D[16];

		protected const float minMoveDistance = 0.001f;
		protected const float shellRadius = 0.01f;


		private /*readonly*/ int m_platformLayer;
		private const float m_platformTopEpsilon = 0.1f;

		private const float m_nearGroundDistance = 1.0f;


		/// <summary>
		/// Bounce the object's vertical velocity.
		/// </summary>
		/// <param name="value"></param>
		public void Bounce(float value)
		{
			velocity.y = value;
		}

		/// <summary>
		/// Bounce the objects velocity in a direction.
		/// </summary>
		/// <param name="dir"></param>
		public virtual void Bounce(Vector2 dir)
		{
			velocity = dir;
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

		protected virtual void OnEnable()
		{
			body = GetComponent<Rigidbody2D>();
			body.isKinematic = true;
		}

		protected virtual void OnDisable()
		{
			body.isKinematic = false;
		}

		protected virtual void Start()
		{
			contactFilter.useTriggers = false;
			m_platformLayer = LayerMask.NameToLayer("OneWayPlatforms");
			Assert.AreNotEqual(m_platformLayer, -1);
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
			//if already falling, fall faster than the jump speed, otherwise use normal gravity.
			velocity += (IsWallClinging ? m_wallClingGravityScalar : 1.0f) * (velocity.y < 0 ? gravityModifier : 1.0f) * Physics2D.gravity * Time.deltaTime;

			velocity.x = targetVelocity.x;

			IsGrounded = false;
			IsWallClinging = false;

			Vector2 deltaPosition = velocity * Time.deltaTime;

			Vector2 moveAlongGround = new Vector2(groundNormal.y, -groundNormal.x);

			Vector2 move = moveAlongGround * deltaPosition.x;

			// update our collision mask
			bool shouldIgnoreOneWays = velocity.y >= 0 || /*(GetComponent<AnimationController>() != null && GetComponent<AnimationController>().move.y < 0.0f) ||*/ (GetComponent<AvatarController>() != null && Input.GetAxis("Vertical") < 0.0f); // TODO: way for AI to drop through platforms
			contactFilter.SetLayerMask(shouldIgnoreOneWays ? Physics2D.GetLayerCollisionMask(gameObject.layer) & ~(1 << m_platformLayer) : Physics2D.GetLayerCollisionMask(gameObject.layer));

			Lazy<bool> isNearGround = new Lazy<bool>(() => Physics2D.Raycast(GetComponent<Collider2D>().bounds.min, Vector2.down, m_nearGroundDistance).collider != null, false); // TODO: cheaper way to avoid starting wall cling when right above the ground?
			PerformMovement(move, false, ref isNearGround);

			move = Vector2.up * deltaPosition.y;

			PerformMovement(move, true, ref isNearGround);
		}

		public bool ShouldIgnore(Rigidbody2D body, Collider2D collider, bool ignoreStatics, bool ignoreDynamics)
		{
			Assert.IsNotNull(collider);
			GameObject otherObj = collider.gameObject;
			if (otherObj == gameObject)
			{
				return true; // ignore our own object
			}
			if (ignoreStatics && (body == null || body.bodyType == RigidbodyType2D.Static))
			{
				return true;
			}
			if (ignoreDynamics && body != null && body.bodyType == RigidbodyType2D.Dynamic)
			{
				return true;
			}
			for (Transform transformItr = otherObj.transform; transformItr != null; transformItr = transformItr.parent)
			{
				if (transformItr == transform)
				{
					return true; // ignore child objects
				}
			}
			if (Physics2D.GetIgnoreCollision(GetComponent<Collider2D>(), collider))
			{
				return true; // ignore objects flagged to ignore each other
			}
			return false;
		}


		void PerformMovement(Vector2 move, bool yMovement, ref Lazy<bool> isNearGround)
		{
			float distance = move.magnitude;

			if (distance >= minMoveDistance)
			{
				//check if we hit anything in current direction of travel
				int count = body.Cast(move, contactFilter, hitBuffer, distance + shellRadius);
				for (int i = 0; i < count; i++)
				{
					RaycastHit2D hit = hitBuffer[i];
					if (ShouldIgnore(hit.rigidbody, hit.collider, false, true))
					{
						continue; // don't get hung up on dynamic/carried/ignored objects
					}
					if (hit.collider.gameObject.layer == m_platformLayer && GetComponent<Collider2D>().bounds.min.y + m_platformTopEpsilon < hit.collider.bounds.max.y)
					{
						continue; // if partway through a one-way platform, ignore it
					}

					Vector2 currentNormal = hit.normal;

					//is this surface flat enough to land on?
					if (currentNormal.y >= minGroundNormalY)
					{
						IsGrounded = true;
						IsWallClinging = false;
						// if moving up, change the groundNormal to new surface normal.
						if (yMovement)
						{
							groundNormal = currentNormal;
							currentNormal.x = 0;
						}
					}
					if (!IsGrounded && currentNormal.y >= m_minWallClingNormalY && !isNearGround.Value)
					{
						IsWallClinging = true;
						m_wallNormal = currentNormal;
					}
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
						//We are airborne, but hit something, so cancel vertical up and horizontal velocity.
						velocity.x *= 0;
						velocity.y = Mathf.Min(velocity.y, 0);
					}
					//remove shellDistance from actual move distance.
					float modifiedDistance = hit.distance - shellRadius;
					distance = modifiedDistance < distance ? modifiedDistance : distance;
				}
			}
			body.position += move.normalized * distance;
		}
	}
}
