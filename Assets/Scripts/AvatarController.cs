using Platformer.Gameplay;
using UnityEngine;
using static Platformer.Core.Simulation;


namespace Platformer.Mechanics
{
	/// <summary>
	/// This is the main class used to implement control of the avatar.
	/// </summary>
	[RequireComponent(typeof(Health))]
	public class AvatarController : AnimationController
	{
		public enum JumpState
		{
			Grounded,
			PrepareToJump,
			Jumping,
			InFlight,
			Landed
		}


		public AudioClip jumpAudio;
		public AudioClip respawnAudio;

		public JumpState jumpState = JumpState.Grounded;
		public Health health;
		public bool controlEnabled = true;

		public bool IsPickingUp { get; private set; }


		private GameObject m_focusObj;


		protected override void Awake()
		{
			base.Awake();
			health = GetComponent<Health>();
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
				}

				// determine current focus object
				// TODO: more nuanced prioritization?
				m_focusObj = null;
				float radius = GetComponent<CircleCollider2D>().radius;
				Collider2D[] focusCandidates = Physics2D.OverlapCircleAll((Vector2)transform.position + Vector2.right * (LeftFacing ? -1.0f : 1.0f) * radius, radius); // TODO: restrict to certain layers?
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

				if (transform.childCount > 0)
				{
					// manipulate first held item
					ItemController item = GetComponentInChildren<ItemController>();

					// swing
					if (Input.GetButtonDown("Fire1"))
					{
						item.Swing();
					}

					// aim
					item.UpdateAim(Camera.main.ScreenToWorldPoint(Input.mousePosition), radius);

					// throw
					if (Input.GetButtonDown("Fire2"))
					{
						// TODO: show aim indicator?
					}
					else if (Input.GetButtonUp("Fire2"))
					{
						// release
						item.Throw();
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


		private void UpdateJumpState()
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
						Schedule<AvatarJumped>().avatar = this;
						jumpState = JumpState.InFlight;
					}
					break;
				case JumpState.InFlight:
					if (IsGrounded)
					{
						jumpState = JumpState.Landed;
					}
					break;
				case JumpState.Landed:
					jumpState = JumpState.Grounded;
					break;
			}
		}
	}
}
