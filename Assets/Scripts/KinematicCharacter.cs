using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;


/// <summary>
/// KinematicCharacter integrates physics and animation. It is generally used for simple enemy animation.
/// </summary>
[RequireComponent(typeof(SpriteRenderer), typeof(Animator), typeof(Collider2D)), RequireComponent(typeof(AudioSource))]
public abstract class KinematicCharacter : KinematicObject
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
	public float m_wallJumpXYRatio = 1.0f;

	/// <summary>
	/// A jump modifier applied to slow down an active jump when
	/// the user releases the jump input.
	/// </summary>
	public float jumpDeceleration = 0.5f;

	/// <summary>
	/// A velocity directed and sent to Bounce() when taking nonlethal damage
	/// </summary>
	public Vector2 m_damageBounceMagnitude = new(3.5f, 3.5f);

	public AudioClip ouchAudio;
	public AudioClip m_deathAudio;

	public int MaxPickUps => GetComponentsInChildren<IHolder>().Sum(holder => holder.HoldCountMax);


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
	protected bool jump;

	/// <summary>
	/// Set to true to set the current jump velocity to zero.
	/// </summary>
	protected bool stopJump;


	private SpriteRenderer spriteRenderer;
	protected Animator animator;
	protected AudioSource audioSource;


	public bool LeftFacing => spriteRenderer.flipX;

	public bool IsPickingUp { get; protected set; }

	public bool IsDropping => move.y < 0.0f;


	protected virtual void Awake()
	{
		spriteRenderer = GetComponent<SpriteRenderer>();
		animator = GetComponent<Animator>();
		audioSource = GetComponent<AudioSource>();
	}

	protected override void ComputeVelocity()
	{
		if (jump && (IsGrounded || IsWallClinging))
		{
			if (IsWallClinging)
			{
				Bounce(new Vector2(m_wallNormal.x * maxSpeed, 0.0f)); // TODO: fix blending w/ directional input
				velocity += jumpTakeOffSpeed * new Vector2(move.x < 0.0f ? -m_wallJumpXYRatio : m_wallJumpXYRatio, 1.0f).normalized; // NOTE that we purposely incorporate any existing velocity so that gravity will eventually take over and prevent clinging to the walls forever
			}
			else
			{
				velocity.y = jumpTakeOffSpeed; // NOTE that we purposely ignore any existing velocity so that ground-based jumps are always full strength
			}
			jump = false;
		}
		else if (stopJump)
		{
			stopJump = false;
			if (velocity.y > 0)
			{
				velocity.y *= jumpDeceleration;
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

		animator.SetBool("grounded", IsGrounded);
		animator.SetBool("wallCling", IsWallClinging);
		animator.SetBool("aimReverse", m_aimDir != 0 && (velocity.x < 0.0f) != (m_aimDir < 0));
		animator.SetFloat("velocityX", Mathf.Abs(velocity.x) / maxSpeed);

		targetVelocity = move * maxSpeed;
	}


	public virtual void OnDamage(GameObject source)
	{
		EnableCollision.TemporarilyDisableCollision(source.GetComponents<Collider2D>(), new Collider2D[] { m_collider });

		// knock away from source
		Vector2 bounceVecOriented = transform.position.x < source.transform.position.x ? new(-m_damageBounceMagnitude.x, m_damageBounceMagnitude.y) : m_damageBounceMagnitude;
		Bounce(bounceVecOriented);

		if (audioSource && ouchAudio)
		{
			audioSource.PlayOneShot(ouchAudio);
		}
	}

	public virtual bool OnDeath()
	{
		// TODO: early-out if already dead?

		// detach all items
		foreach (ItemController item in GetComponentsInChildren<ItemController>())
		{
			item.Detach();
		}

		foreach (ArmController arm in GetComponentsInChildren<ArmController>())
		{
			arm.gameObject.SetActive(false);
		}

		if (audioSource && m_deathAudio)
		{
			audioSource.PlayOneShot(m_deathAudio);
		}

		animator.SetTrigger("hurt");
		animator.SetTrigger("startDeath");
		animator.SetBool("dead", true);

		m_collider.enabled = false;
		body.simulated = false;

		Bounce(Vector2.zero); // to remove any current forced input

		return true;
	}

	public void AttachItem(IInteractable attachee)
	{
		if (attachee is BackpackController backpack) // TODO: handle through IAttachable interface?
		{
			// allow only one pack at a time
			BackpackController packExisting = GetComponentInChildren<BackpackController>();
			if (packExisting != null)
			{
				packExisting.Detach();
				// TODO: transfer items from old to new pack?
			}

			backpack.AttachTo(this);
			return;
		}

		// TODO: don't assume non-backpack attachables are items?
		ItemController item = (ItemController)attachee;

		ItemController[] heldItems = GetComponentsInChildren<ItemController>().ToArray();
		if (heldItems.Length >= MaxPickUps)
		{
			// TODO: ensure cycling through items?
			heldItems.First().Detach();
		}

		foreach (IHolder holder in GetComponentsInChildren<IHolder>())
		{
			bool held = holder.ItemAttach(item);
			if (held)
			{
				break;
			}
		}
	}


	// called by animation triggers
	private void ProcessAnimEvent(AnimationEvent evt)
	{
		Assert.IsNotNull(evt.objectReferenceParameter);
		Assert.IsTrue(evt.objectReferenceParameter.GetType() == typeof(AudioClip));
		if (audioSource)
		{
			audioSource.PlayOneShot((AudioClip)evt.objectReferenceParameter);
		}
	}
}
