using Platformer.Gameplay;
using System.Linq;
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
			WallCling,
			Landed
		}


		public AudioClip jumpAudio;
		public AudioClip respawnAudio;

		public GameObject m_focusIndicator;

		public float m_coyoteTime = 0.15f;
		public float m_xInputForcedSmoothTime = 0.25f;

		private JumpState jumpState = JumpState.Grounded;
		private Health health;
		public bool controlEnabled = true;

		public bool IsPickingUp { get; private set; }


		private float m_leftGroundTime = -1.0f;

		private float m_xInputForced = 0.0f;
		private float m_xInputForcedVel;

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
				if ((jumpState == JumpState.Grounded || jumpState == JumpState.WallCling || m_leftGroundTime + m_coyoteTime <= Time.time) && Input.GetButtonDown("Jump"))
				{
					jumpState = JumpState.PrepareToJump;
					if (IsWallClinging)
					{
						m_xInputForced = m_wallNormal.x;
						m_xInputForcedVel = 0.0f;
					}
				}
				else if (Input.GetButtonUp("Jump"))
				{
					stopJump = true;
				}
				move.x = Mathf.Lerp(Input.GetAxis("Horizontal"), m_xInputForced, Mathf.Abs(m_xInputForced));

				// blend x-input back from forced if necessary
				m_xInputForced = Mathf.SmoothDamp(m_xInputForced, 0.0f, ref m_xInputForcedVel, m_xInputForcedSmoothTime);

				// determine current focus object
				// TODO: more nuanced prioritization?
				m_focusObj = null;
				float radius = GetComponent<CircleCollider2D>().radius;
				Collider2D[] focusCandidates = Physics2D.OverlapCircleAll((Vector2)transform.position + Vector2.right * (LeftFacing ? -1.0f : 1.0f) * radius, radius * 1.5f); // TODO: restrict to certain layers?
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

				// place focus indicator if appropriate
				ItemController focusItem = m_focusObj?.GetComponent<ItemController>();
				if (focusItem != null)
				{
					BoxCollider2D[] colliders = focusItem.GetComponents<BoxCollider2D>(); // TODO: handle other collider shapes?
					float yMax = colliders.Max(collider => collider.bounds.max.y + collider.edgeRadius) + 0.1f;
					Vector3 pos = m_focusObj.transform.position;
					pos.y = yMax;
					m_focusIndicator.transform.position = pos;
				}
				m_focusIndicator.SetActive(focusItem != null);

				// pick up / drop items
				const int maxPickUps = 2; // TODO: determine based on current inventory/gear
				if (focusItem != null && Input.GetButtonDown("PickUp"))
				{
					focusItem.AttachTo(gameObject);
					m_focusObj = null;
					if (transform.childCount > maxPickUps)
					{
						// drop first attached to cycle through items
						transform.GetChild(0).GetComponent<ItemController>().Detach();
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

				Vector3 mousePosWS = Camera.main.ScreenToWorldPoint(Input.mousePosition);
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
					item.UpdateAim(mousePosWS, radius);

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
				m_aimDir = mousePosWS.x > transform.position.x ? 1 : -1;
			}
			else
			{
				move.x = 0;
			}
			UpdateJumpState();
			base.Update();
		}

		/// <summary>
		/// Bounce the objects velocity in a direction.
		/// </summary>
		/// <param name="dir"></param>
		public override void Bounce(Vector2 dir)
		{
			base.Bounce(dir);
			m_xInputForced = dir.x;
			m_xInputForcedVel = 0.0f;
		}

		public override void OnDeath()
		{
			base.OnDeath();
			controlEnabled = false;
			m_xInputForced = 0.0f;
			Schedule<AvatarSpawn>(2.0f).avatar = this;
		}

		private static readonly Vector2 m_collisionBounceVec = new Vector2(1.0f, 2.5f);
		public void OnCollision(EnemyController enemy)
		{
			health.Decrement();
			Vector2 bounceVecOriented = transform.position.x - enemy.transform.position.x < 0.0f ? new Vector2(-m_collisionBounceVec.x, m_collisionBounceVec.y) : m_collisionBounceVec;
			Bounce(bounceVecOriented);
		}

		public void OnSpawn()
		{
			collider2d.enabled = true;
			body.simulated = true;
			controlEnabled = false;

			if (audioSource && respawnAudio)
			{
				audioSource.PlayOneShot(respawnAudio);
			}

			health.Respawn();

			Teleport(Vector3.zero);
			jumpState = JumpState.Grounded;

			animator.SetBool("dead", false);

			Schedule<EnablePlayerInput>(2f).avatar = this;
		}


		private void UpdateJumpState()
		{
			jump = false;
			switch (jumpState)
			{
				case JumpState.Grounded:
					if (!IsGrounded)
					{
						jumpState = JumpState.InFlight;
						m_leftGroundTime = Time.time;
					}
					break;
				case JumpState.PrepareToJump:
					jumpState = JumpState.Jumping;
					jump = true;
					stopJump = false;
					break;
				case JumpState.Jumping:
					if (!IsGrounded)
					{
						if (audioSource && jumpAudio)
						{
							audioSource.PlayOneShot(jumpAudio);
						}
						jumpState = JumpState.InFlight;
					}
					break;
				case JumpState.InFlight:
					if (IsGrounded)
					{
						jumpState = JumpState.Landed;
					}
					else if (IsWallClinging)
					{
						jumpState = JumpState.WallCling;
					}
					break;
				case JumpState.WallCling:
					if (!IsWallClinging)
					{
						jumpState = JumpState.InFlight;
						m_leftGroundTime = Time.time;
					}
					break;
				case JumpState.Landed:
					jumpState = JumpState.Grounded;
					break;
			}
		}
	}
}
