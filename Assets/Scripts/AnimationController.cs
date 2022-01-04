using UnityEngine;
using UnityEngine.Assertions;


namespace Platformer.Mechanics
{
	/// <summary>
	/// AnimationController integrates physics and animation. It is generally used for simple enemy animation.
	/// </summary>
	[RequireComponent(typeof(SpriteRenderer), typeof(Animator), typeof(Collider2D)), RequireComponent(typeof(AudioSource))]
	public class AnimationController : KinematicObject
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

		public AudioClip ouchAudio;
		public AudioClip m_deathAudio;

		public int m_maxPickUps = 0; // TODO: determine based on current inventory/gear


		/// <summary>
		/// Used to indicated desired direction of travel.
		/// </summary>
		protected Vector2 move;

		/// <summary>
		/// Used to indicated desired direction of aim.
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


		SpriteRenderer spriteRenderer;
		protected Animator animator;
		protected AudioSource audioSource;
		protected Collider2D collider2d;


		public bool LeftFacing => spriteRenderer.flipX;

		public bool IsPickingUp { get; protected set; }

		public bool IsDropping => move.y < 0.0f;


		protected virtual void Awake()
		{
			spriteRenderer = GetComponent<SpriteRenderer>();
			animator = GetComponent<Animator>();
			audioSource = GetComponent<AudioSource>();
			collider2d = GetComponent<Collider2D>();
		}

		protected override void ComputeVelocity()
		{
			if (jump && (IsGrounded || IsWallClinging))
			{
				if (IsWallClinging)
				{
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


		public virtual void OnDamage()
		{
			if (audioSource && ouchAudio)
			{
				audioSource.PlayOneShot(ouchAudio);
			}
		}

		public virtual void OnDeath()
		{
			// detach all items
			foreach (ItemController item in GetComponentsInChildren<ItemController>())
			{
				item.Detach();
			}

			if (audioSource && m_deathAudio)
			{
				audioSource.PlayOneShot(m_deathAudio);
			}

			animator.SetTrigger("hurt");
			animator.SetTrigger("startDeath");
			animator.SetBool("dead", true);

			collider2d.enabled = false;
			body.simulated = false;
		}


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
}
