using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;


/// <summary>
/// KinematicCharacter integrates physics and animation. It is generally used for simple enemy animation.
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
	/// A velocity directed and sent to Bounce() when taking nonlethal damage
	/// </summary>
	public Vector2 m_damageBounceMagnitude = new(3.5f, 3.5f);

	public Vector2 m_armOffset;


	/// <summary>
	/// Used to indicate desired direction of travel.
	/// </summary>
	public Vector2 move; // TODO: un-expose?

	/// <summary>
	/// Used to indicate desired direction of aim.
	/// </summary>
	protected int m_aimDir;

	/// <summary>
	/// Set to true to initiate a jump.
	/// </summary>
	protected bool m_jump;

	/// <summary>
	/// Set to true to decelerate the current jump using m_jumpDeceleration.
	/// </summary>
	protected bool m_stopJump;


	private SpriteRenderer spriteRenderer;
	protected Animator animator;
	protected AudioSource audioSource;


	public int HoldCountMax => GetComponentsInChildren<IHolder>().Where(holder => holder.Component != this).Sum(holder => holder.HoldCountMax);

	public Vector3 AttachOffsetLocal => Vector3.zero;
	public Vector3 ChildAttachPointLocal => Vector3.zero;

	public bool LeftFacing => spriteRenderer.flipX;

	public bool IsPickingUp { get; protected set; }

	public bool IsDropping => move.y < -0.5f;


	private bool m_wasGrounded;


	protected override void Awake()
	{
		base.Awake();
		spriteRenderer = GetComponent<SpriteRenderer>();
		animator = GetComponent<Animator>();
		audioSource = GetComponent<AudioSource>();
	}

	protected override void ComputeVelocity()
	{
		if (m_jump)
		{
			if (IsGrounded)
			{
				velocity.y = jumpTakeOffSpeed; // NOTE that we purposely ignore any existing velocity so that ground-based jumps are always full strength
			}
			else
			{
				// wall jump if we are in wall cling or find a near-enough wall
				bool canWallJump = IsWallClinging;
				if (!canWallJump) // TODO: also skip when only slightly above ground/platform?
				{
					List<ContactPoint2D> contacts = new();
					m_collider.GetContacts(contacts);
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
					Bounce(Quaternion.Euler(0.0f, 0.0f, m_wallNormal.x < 0.0f ? -m_wallJumpDegrees : m_wallJumpDegrees) * m_wallNormal * jumpTakeOffSpeed);
				}
			}
			m_jump = false;
		}
		else if (m_stopJump)
		{
			m_stopJump = false;
			if (velocity.y > 0.0f)
			{
				velocity.y *= m_jumpDeceleration;
			}
		}

		if (m_aimDir > 0 || (m_aimDir == 0 && move.x >= minMoveDistance))
		{
			spriteRenderer.flipX = false;
		}
		else if (m_aimDir < 0 || (m_aimDir == 0 && move.x <= -minMoveDistance))
		{
			spriteRenderer.flipX = true;
		}

		animator.SetBool("grounded", IsGrounded || m_wasGrounded);
		m_wasGrounded = IsGrounded;
		animator.SetBool("wallCling", IsWallClinging);
		animator.SetBool("aimReverse", m_aimDir != 0 && (velocity.x < 0.0f) != (m_aimDir < 0));
		animator.SetFloat("velocityX", Mathf.Abs(velocity.x) / (maxSpeed == 0.0f ? 1.0f : maxSpeed));

		targetVelocity = move * maxSpeed;
	}


	public virtual void OnDamage(GameObject source)
	{
		// knock away from source
		Vector2 bounceVecOriented = transform.position.x < source.transform.position.x ? new(-m_damageBounceMagnitude.x, m_damageBounceMagnitude.y) : m_damageBounceMagnitude;
		Bounce(bounceVecOriented);
	}

	public virtual bool OnDeath()
	{
		// TODO: early-out if already dead?

		DetachAll();

		animator.SetTrigger("hurt");
		animator.SetTrigger("startDeath");
		animator.SetBool("dead", true);

		m_collider.enabled = false;
		body.simulated = false;

		Bounce(Vector2.zero); // to remove any current forced input

		return true;
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

	public void ChildDetach(IAttachable attachable, bool noAutoReplace)
	{
		IHolder.ChildDetachInternal(attachable, this, noAutoReplace);
	}

	public void DetachAll()
	{
		foreach (IAttachable attachee in GetComponentsInChildren<IAttachable>())
		{
			attachee.Detach(true);
		}
	}

	public virtual bool CanDamage(GameObject target)
	{
		return gameObject != target;
	}

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
