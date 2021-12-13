using UnityEngine;


namespace Platformer.Mechanics
{
	/// <summary>
	/// AnimationController integrates physics and animation. It is generally used for simple enemy animation.
	/// </summary>
	[RequireComponent(typeof(SpriteRenderer), typeof(Animator), typeof(AudioSource))]
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
		/// A jump modifier applied to initial jump velocities.
		/// </summary>
		public float jumpModifier = 1.5f;

		/// <summary>
		/// A jump modifier applied to slow down an active jump when
		/// the user releases the jump input.
		/// </summary>
		public float jumpDeceleration = 0.5f;

		public AudioClip ouchAudio;


		/// <summary>
		/// Used to indicated desired direction of travel.
		/// </summary>
		protected Vector2 move;

		/// <summary>
		/// Set to true to initiate a jump.
		/// </summary>
		protected bool jump;

		/// <summary>
		/// Set to true to set the current jump velocity to zero.
		/// </summary>
		protected bool stopJump;


		SpriteRenderer spriteRenderer;
		internal Animator animator;
		public AudioSource audioSource;


		public bool LeftFacing => spriteRenderer.flipX;


		protected virtual void Awake()
		{
			spriteRenderer = GetComponent<SpriteRenderer>();
			animator = GetComponent<Animator>();
			audioSource = GetComponent<AudioSource>();
		}

		protected override void ComputeVelocity()
		{
			if (jump && IsGrounded)
			{
				velocity.y = jumpTakeOffSpeed * jumpModifier;
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

			if (move.x >= minMoveDistance)
			{
				spriteRenderer.flipX = false;
			}
			else if (move.x <= -minMoveDistance)
			{
				spriteRenderer.flipX = true;
			}

			animator.SetBool("grounded", IsGrounded);
			animator.SetFloat("velocityX", Mathf.Abs(velocity.x) / maxSpeed);

			targetVelocity = move * maxSpeed;
		}
	}
}
