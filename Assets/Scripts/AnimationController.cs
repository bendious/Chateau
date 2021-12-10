using Platformer.Core;
using Platformer.Model;
using UnityEngine;


namespace Platformer.Mechanics
{
	/// <summary>
	/// AnimationController integrates physics and animation. It is generally used for simple enemy animation.
	/// </summary>
	[RequireComponent(typeof(SpriteRenderer), typeof(Animator))]
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
		/// Used to indicated desired direction of travel.
		/// </summary>
		public Vector2 move;

		/// <summary>
		/// Set to true to initiate a jump.
		/// </summary>
		public bool jump;

		/// <summary>
		/// Set to true to set the current jump velocity to zero.
		/// </summary>
		public bool stopJump;


		SpriteRenderer spriteRenderer;
		internal Animator animator;
		readonly PlatformerModel model = Simulation.GetModel<PlatformerModel>();


		public bool LeftFacing => spriteRenderer.flipX;


		protected virtual void Awake()
		{
			spriteRenderer = GetComponent<SpriteRenderer>();
			animator = GetComponent<Animator>();
		}

		protected override void ComputeVelocity()
		{
			if (jump && IsGrounded)
			{
				velocity.y = jumpTakeOffSpeed * model.jumpModifier;
				jump = false;
			}
			else if (stopJump)
			{
				stopJump = false;
				if (velocity.y > 0)
				{
					velocity.y *= model.jumpDeceleration;
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
