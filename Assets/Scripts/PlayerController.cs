using Platformer.Core;
using Platformer.Gameplay;
using Platformer.Model;
using UnityEngine;
using static Platformer.Core.Simulation;


namespace Platformer.Mechanics
{
	/// <summary>
	/// This is the main class used to implement control of the player.
	/// It is a superset of the AnimationController class, but is inlined to allow for any kind of customisation.
	/// </summary>
	[RequireComponent(typeof(SpriteRenderer), typeof(Collider2D), typeof(Animator)), RequireComponent(typeof(AudioSource), typeof(Health))]
	public class PlayerController : KinematicObject
	{
		public AudioClip jumpAudio;
		public AudioClip respawnAudio;
		public AudioClip ouchAudio;

		/// <summary>
		/// Max horizontal speed of the player.
		/// </summary>
		public float maxSpeed = 7;
		/// <summary>
		/// Initial jump velocity at the start of a jump.
		/// </summary>
		public float jumpTakeOffSpeed = 5.0f;

		public JumpState jumpState = JumpState.Grounded;
		private bool stopJump;
		/*internal new*/ public Collider2D collider2d;
		/*internal new*/ public AudioSource audioSource;
		public Health health;
		public bool controlEnabled = true;

		bool jump;
		Vector2 move;
		SpriteRenderer spriteRenderer;
		internal Animator animator;
		readonly PlatformerModel model = GetModel<PlatformerModel>();

		public Bounds Bounds => collider2d.bounds;

		public bool IsPickingUp { get; private set; }


		private GameObject m_focusObj;


		void Awake()
		{
			health = GetComponent<Health>();
			audioSource = GetComponent<AudioSource>();
			collider2d = GetComponent<Collider2D>();
			spriteRenderer = GetComponent<SpriteRenderer>();
			animator = GetComponent<Animator>();
		}

		protected override void Update()
		{
			if (controlEnabled)
			{
				move.x = Input.GetAxis("Horizontal");
				if (jumpState == JumpState.Grounded && Input.GetButtonDown("Jump"))
				{
					jumpState = JumpState.PrepareToJump;
				}
				else if (Input.GetButtonUp("Jump"))
				{
					stopJump = true;
					Schedule<PlayerStopJump>().player = this;
				}

				// determine current focus object
				// TODO: more nuanced prioritization?
				m_focusObj = null;
				float radius = GetComponent<CircleCollider2D>().radius;
				Collider2D[] focusCandidates = Physics2D.OverlapCircleAll((Vector2)transform.position + Vector2.right * (spriteRenderer.flipX ? -1.0f : 1.0f) * radius, radius); // TODO: restrict to certain layers?
				float distSqFocus = float.MaxValue;
				foreach (Collider2D candidate in focusCandidates)
				{
					if (ShouldIgnore(candidate.GetComponent<Rigidbody2D>(), candidate, false, false))
					{
						continue; // ignore ourself / attached/ignored objects
					}

					float distSqCur = (transform.position - candidate.transform.position).sqrMagnitude;
					if (distSqCur < distSqFocus)
					{
						distSqFocus = distSqCur;
						m_focusObj = candidate.gameObject;
					}
				}

				// pick up / drop items
				const int maxPickUps = 2; // TODO: determine based on current inventory/gear
				if (Input.GetButtonDown("PickUp"))
				{
					if (m_focusObj != null)
					{
						ItemController item = m_focusObj.GetComponent<ItemController>();
						if (item != null)
						{
							item.AttachTo(gameObject);
							m_focusObj = null;
							if (transform.childCount > maxPickUps)
							{
								// drop first attached to cycle through items
								transform.GetChild(0).GetComponent<ItemController>().Detach();
							}
						}
					}
				}
				IsPickingUp = Input.GetButton("PickUp") && transform.childCount < maxPickUps;

				if (Input.GetButtonDown("Drop"))
				{
					if (transform.childCount > 0)
					{
						GetComponentInChildren<ItemController>().Detach();
					}
				}
			}
			else
			{
				move.x = 0;
			}
			UpdateJumpState();
			base.Update();
		}

		void UpdateJumpState()
		{
			jump = false;
			switch (jumpState)
			{
				case JumpState.PrepareToJump:
					jumpState = JumpState.Jumping;
					jump = true;
					stopJump = false;
					break;
				case JumpState.Jumping:
					if (!IsGrounded)
					{
						Schedule<PlayerJumped>().player = this;
						jumpState = JumpState.InFlight;
					}
					break;
				case JumpState.InFlight:
					if (IsGrounded)
					{
						Schedule<PlayerLanded>().player = this;
						jumpState = JumpState.Landed;
					}
					break;
				case JumpState.Landed:
					jumpState = JumpState.Grounded;
					break;
			}
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

			if (move.x > 0.01f)
			{
				spriteRenderer.flipX = false;
			}
			else if (move.x < -0.01f)
			{
				spriteRenderer.flipX = true;
			}

			animator.SetBool("grounded", IsGrounded);
			animator.SetFloat("velocityX", Mathf.Abs(velocity.x) / maxSpeed);

			targetVelocity = move * maxSpeed;
		}

		public enum JumpState
		{
			Grounded,
			PrepareToJump,
			Jumping,
			InFlight,
			Landed
		}
	}
}
