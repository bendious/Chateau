using System.Collections.Generic;
using System.Linq;
using UnityEngine;


/// <summary>
/// KinematicCharacter integrates physics and animation, generally for simple characters.
/// </summary>
[RequireComponent(typeof(SpriteRenderer), typeof(Animator), typeof(Collider2D)), RequireComponent(typeof(AudioSource))]
public abstract class KinematicCharacter : KinematicObject, IHolder
{
	/// <summary>
	/// Max horizontal speed.
	/// </summary>
	public float maxSpeed = 7;

	/// <summary>
	/// Max jump velocity
	/// </summary>
	public float jumpTakeOffSpeed = 7;

	/// <summary>
	/// Ratio governing the horizontal/vertical direction of wall jumps.
	/// </summary>
	public float m_wallJumpDegrees = 45.0f;

	/// <summary>
	/// A jump modifier applied to slow down an active jump when
	/// the user releases the jump input.
	/// </summary>
	public float m_jumpDeceleration = 0.5f;

	/// <summary>
	/// A multiplier applied to damage attributed to this character
	/// </summary>
	public float m_damageScalar = 1.0f;

	/// <summary>
	/// Max dash velocity
	/// </summary>
	[SerializeField] private Vector2 m_dashVelocity = new(25.0f, 0.0f);

	/// <summary>
	/// Dash x/y component durations
	/// </summary>
	[SerializeField] private Vector2 m_dashSecondsXY = new(0.2f, 0.2f);

	/// <summary>
	/// Whether dash confers invulnerability
	/// </summary>
	[SerializeField] private bool m_dashInvincibility;

	/// <summary>
	/// Alternate to m_contactDamage used while dashing
	/// </summary>
	[SerializeField] private float m_dashDamage = 0.0f;

	/// <summary>
	/// Dash SFX audio clips
	/// </summary>
	[SerializeField] private WeightedObject<AudioClip>[] m_dashSFX;

	/// <summary>
	/// A velocity directed and sent to Bounce() when taking nonlethal damage
	/// </summary>
	public Vector2 m_damageBounceMagnitude = new(3.5f, 3.5f);

	[SerializeField] private Vector2 m_armOffset;

	/// <summary>
	/// Amount of damage caused to other characters that collide with this one
	/// </summary>
	[SerializeField] private float m_contactDamage = 1.0f;

	/// <summary>
	/// Type of damage caused to other characters that collide with this one
	/// </summary>
	[SerializeField] private Health.DamageType m_contactDamageType = Health.DamageType.Blunt;


	/// <summary>
	/// Used to indicate desired direction of travel.
	/// </summary>
	public Vector2 move; // TODO: un-expose?

	/// <summary>
	/// Used to indicate desired direction of aim.
	/// </summary>
	protected int m_aimDir;

	/// <summary>
	/// Set to initiate a jump. 1.0 -> full jump takeoff speed, 0.5 -> half speed, etc.
	/// </summary>
	protected float m_jump;

	/// <summary>
	/// Set to true to decelerate the current jump using m_jumpDeceleration.
	/// </summary>
	protected bool m_stopJump;

	/// <summary>
	/// Set to true to initiate a dash.
	/// </summary>
	protected bool m_dash;


	private SpriteRenderer m_spriteRenderer;
	protected Animator m_animator;
	protected AudioSource m_audioSource;
	protected Health m_health;


	public int HoldCountMax => GetComponentsInChildren<IHolder>().Where(holder => holder.Component != this).Sum(holder => holder.HoldCountMax);

	public Vector3 AttachOffsetLocal => Vector3.zero;
	public Vector3 ChildAttachPointLocal => Vector3.zero;

	public bool LeftFacing => m_spriteRenderer.flipX;

	public Vector2 ArmOffset => LeftFacing ? new(-m_armOffset.x, m_armOffset.y) : m_armOffset;

	public bool IsPickingUp { get; protected set; }

	public bool IsDropping => move.y < -0.5f && maxSpeed > 0.0f;


	private bool m_wasGrounded;

	private bool m_isDashing = false;
	private bool m_dashAvailable = true;


	protected override void Awake()
	{
		base.Awake();

		m_spriteRenderer = GetComponent<SpriteRenderer>();
		m_animator = GetComponent<Animator>();
		m_audioSource = GetComponent<AudioSource>();
		m_health = GetComponent<Health>();

		OnHealthDecrement.OnExecute += OnDamage;
		OnHealthDeath.OnExecute += OnDeath;
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		KinematicCharacter character = collision.collider.GetComponent<KinematicCharacter>(); // NOTE that we use the collider object rather than collision.gameObject since w/ characters & arms, they are not always the same
		if (character != null)
		{
			OnCharacterCollision(character);
		}
	}

	private void OnCollisionStay2D(Collision2D collision)
	{
		OnCollisionEnter2D(collision);
	}

	protected virtual void OnDestroy()
	{
		OnHealthDecrement.OnExecute -= OnDamage;
		OnHealthDeath.OnExecute -= OnDeath;
	}


	protected override void ComputeVelocity()
	{
		if (m_jump > 0.0f && maxSpeed > 0.0f)
		{
			if (IsGrounded)
			{
				velocity.y = m_jump * jumpTakeOffSpeed; // NOTE that we purposely ignore any existing velocity so that ground-based jumps are always at the intended strength
			}
			else
			{
				// wall jump if we are in wall cling or find a near-enough wall
				bool canWallJump = IsWallClinging;
				if (!canWallJump) // TODO: also skip when only slightly above ground/platform?
				{
					List<ContactPoint2D> contacts = new();
					foreach (Collider2D collider in m_colliders)
					{
						collider.GetContacts(contacts);
					}
					Vector2 wallNormal = contacts.FirstOrDefault(contact =>
					{
						if (contact.rigidbody != null || contact.normal.y < m_minWallClingNormalY)
						{
							return false;
						}
						PlatformEffector2D effector = contact.collider.GetComponent<PlatformEffector2D>();
						return effector == null || !effector.enabled;
					}).normal;
					if (wallNormal != Vector2.zero && wallNormal.y >= m_minWallClingNormalY)
					{
						m_wallNormal = wallNormal;
						canWallJump = true;
					}
				}
				if (canWallJump)
				{
					Bounce(Quaternion.Euler(0.0f, 0.0f, m_wallNormal.x < 0.0f ? -m_wallJumpDegrees : m_wallJumpDegrees) * m_wallNormal * m_jump * jumpTakeOffSpeed);
				}
			}
		}
		else if (m_stopJump)
		{
			if (velocity.y > 0.0f)
			{
				velocity.y *= m_jumpDeceleration;
			}
		}

		if (IsGrounded || IsWallClinging)
		{
			m_dashAvailable = true;
		}
		if (m_dash && m_dashAvailable && !HasForcedVelocity)
		{
			float moveDirX = (IsWallClinging ? m_wallNormal : move).x;
			Bounce(moveDirX < -Utility.FloatEpsilon || (moveDirX.FloatEqual(0.0f) && LeftFacing) ? new(-m_dashVelocity.x, m_dashVelocity.y) : m_dashVelocity, m_dashSecondsXY.x, m_dashSecondsXY.y);
			m_animator.SetBool("dash", true);
			m_audioSource.PlayOneShot(m_dashSFX.RandomWeighted());
			m_dashAvailable = false;
			if (m_dashInvincibility)
			{
				m_health.m_invincible = true;
				Simulation.Schedule<EnableDamage>(1.0f).m_health = m_health; // NOTE that this is just a fallback // TODO: more exact timing?
			}
			m_isDashing = true;
		}
		else if (m_isDashing && !HasForcedVelocity) // TODO: take into account other sources of forced velocity?
		{
			m_isDashing = false;
			m_health.m_invincible = false; // TODO: take into account other sources of invincibility?
			m_animator.SetBool("dash", false);
		}

		m_jump = 0.0f;
		m_stopJump = false;
		m_dash = false;

		if (m_aimDir > 0 || (m_aimDir == 0 && move.x >= minMoveDistance))
		{
			m_spriteRenderer.flipX = false;
		}
		else if (m_aimDir < 0 || (m_aimDir == 0 && move.x <= -minMoveDistance))
		{
			m_spriteRenderer.flipX = true;
		}

		m_animator.SetBool("grounded", IsGrounded || m_wasGrounded);
		m_wasGrounded = IsGrounded;
		m_animator.SetBool("wallCling", IsWallClinging);
		m_animator.SetBool("aimReverse", m_aimDir != 0 && (velocity.x < 0.0f) != (m_aimDir < 0)); // NOTE that we compare x-velocity to 0.0 rather than Utility.FloatEpsilon since reverse aim needs to remain stable as velocity decays
		m_animator.SetFloat("velocityX", Mathf.Abs(velocity.x) / (maxSpeed == 0.0f ? 1.0f : maxSpeed));

		TargetVelocity = move * maxSpeed;
	}

	protected virtual bool OnCharacterCollision(KinematicCharacter character)
	{
		// temporarily disable collision to prevent getting stuck
		// NOTE that this is BEFORE the early-out
		// TODO: disable w/ all characters rather than just this one? ensure consistent re-enable time? don't double-disable collision due to both characters' OnCharacterCollision()?
		EnableCollision.TemporarilyDisableCollision(GetComponentsInChildren<Collider2D>(), character.GetComponentsInChildren<Collider2D>());

		float damage = m_isDashing ? m_dashDamage : m_contactDamage;
		if (damage.FloatEqual(0.0f) || !CanDamage(character.gameObject))
		{
			return false;
		}

		return character.m_health.Decrement(gameObject, gameObject, damage, m_contactDamageType);
	}


	private void OnDamage(OnHealthDecrement evt)
	{
		if (evt.m_health.gameObject != gameObject || evt.m_directCause == null || evt.m_amountUnscaled < m_health.m_minorDamageThreshold)
		{
			return;
		}

		// knock away from source
		// TODO: differentiate between source and weapon?
		Vector2 bounceVecOriented = transform.position.x < evt.m_directCause.transform.position.x ? new(-m_damageBounceMagnitude.x, m_damageBounceMagnitude.y) : m_damageBounceMagnitude;
		Bounce(bounceVecOriented);
	}

	private void OnDeath(OnHealthDeath evt)
	{
		if (evt.m_health.gameObject != gameObject)
		{
			return;
		}

		// TODO: early-out if already dead?

		DetachAll();

		m_animator.SetBool("dead", true);

		foreach (Collider2D collider in m_colliders)
		{
			collider.enabled = false;
		}
		body.simulated = false; // TODO: continue simulating physics movement?

		Bounce(Vector2.zero); // to remove any current forced input
	}

	public bool ChildAttach(IAttachable attachee)
	{
		if (attachee is BackpackController backpack) // TODO: handle through IAttachable?
		{
			// allow only one pack at a time
			BackpackController[] packsExisting = GetComponentsInChildren<BackpackController>();
			foreach (BackpackController packExisting in packsExisting)
			{
				if (packExisting.m_attachOffsetLocal == backpack.m_attachOffsetLocal)
				{
					packExisting.Detach(true);
					// TODO: transfer items from old to new pack?
				}
			}

			backpack.AttachInternal(this);
			return true;
		}

		IAttachable[] heldAttachables = GetComponentsInChildren<IAttachable>(true).Where(attachable => attachable is not IHolder).ToArray();
		if (heldAttachables.Length >= HoldCountMax)
		{
			// TODO: ensure cycling through items?
			heldAttachables.First().Detach(true);
		}

		foreach (IHolder holder in GetComponentsInChildren<IHolder>().Where(holder => holder.Component != this))
		{
			bool held = holder.ChildAttach(attachee);
			if (held)
			{
				return true;
			}
		}
		return false;
	}

	public void ChildDetach(IAttachable attachable, bool noAutoReplace) => IHolder.ChildDetachInternal(attachable, this, noAutoReplace);

	public void DetachAll()
	{
		foreach (IAttachable attachee in GetComponentsInChildren<IAttachable>())
		{
			attachee.Detach(true);
		}
	}


	public virtual bool CanDamage(GameObject target) => gameObject != target;

	// TODO: CanBeDamagedBy()?

	public virtual float TargetPriority(KinematicCharacter source, bool friendly) => source != this && source.CanDamage(gameObject) != friendly ? 1.0f : 0.0f;


	[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "called from animation event")]
	private void HideArms(int hideInt)
	{
		bool hide = hideInt != 0; // NOTE that animation triggers can't use bool params for some reason
		bool changed = false;
		foreach (ArmController arm in GetComponentsInChildren<ArmController>(true))
		{
			if (arm.gameObject.activeSelf != hide)
			{
				continue;
			}
			arm.gameObject.SetActive(!hide);
			changed = true;
		}
		if (!hide && changed && this is AvatarController avatar) // TEMP: move into AvatarController?
		{
			avatar.InventorySync();
		}
	}
}
